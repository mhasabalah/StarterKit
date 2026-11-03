using Shared.Core.MultiTenancy;
using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Data;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Domains.Attachments.Features.AssociatePendingAttachments;
using Attachment.Domains.Attachments.Features.CleanupExpiredPendingAttachments;
using Attachment.Domains.Attachments.Features.UploadAttachment;
using Attachment.Domains.Attachments.Features.UploadPendingAttachment;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Attachment.Infrastructure.Validation;
using Attachment.Tests.TestSupport;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Exceptions;

namespace Attachment.Tests.Integration;

[Collection(AttachmentPostgresCollection.Name)]
public sealed class PendingAttachmentFlowTests(AttachmentPostgresFixture db) : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly AttachmentOptions _options = TestOptions.Create(o => o.Pending.MaxUploadsPerSession = 3);

    public void Dispose() => _dir.Dispose();

    private LocalFileStorageService Storage() => TestServices.LocalStorage(_dir.Path, _options);

    private UploadPendingAttachmentHandler UploadHandler(AttachmentDbContext context, Caller caller) =>
        new(new PendingAttachmentRepository(context),
            Storage(),
            new FileValidationService(TestOptions.Wrap(_options), new NoOpMalwareScanner(), NullLogger<FileValidationService>.Instance),
            TestServices.Sessions(caller),
            new Mock<IBackgroundJobClient>().Object,
            TestOptions.Wrap(_options),
            TenantContext.SingleTenant,
            NullLogger<UploadPendingAttachmentHandler>.Instance);

    private static UploadPendingAttachmentCommand Upload(Guid sessionId, int displayOrder)
    {
        var pdf = TestFiles.Pdf();
        return new UploadPendingAttachmentCommand(sessionId, $"doc{displayOrder}.pdf", "application/pdf", pdf.Length,
            new MemoryStream(pdf), AttachmentType.Other, AttachmentCategory.General, displayOrder);
    }

    private async Task<UploadPendingAttachmentResult> UploadAs(Caller caller, Guid sessionId, int displayOrder)
    {
        await using var context = db.CreateContext(caller);
        return await UploadHandler(context, caller).Handle(Upload(sessionId, displayOrder), default);
    }

    [Fact]
    public async Task Server_issued_session_accepts_uploads_and_returns_a_signed_preview_url()
    {
        var caller = Caller.User(Guid.NewGuid());
        var session = TestServices.Sessions(caller).IssueForCurrentCaller();

        var result = await UploadAs(caller, session, 0);

        result.SessionId.Should().Be(session);
        result.FileUrl.Should().StartWith(LocalReadUrlSigner.RoutePath);
        await using var check = db.CreateContext(caller);
        (await check.PendingAttachments.CountAsync(p => p.SessionId == session)).Should().Be(1);
    }

    [Fact]
    public async Task Client_chosen_session_id_is_rejected_and_nothing_is_written()
    {
        var caller = Caller.Anonymous("7.7.7.7");

        await FluentActions.Awaiting(() => UploadAs(caller, Guid.NewGuid(), 0))
            .Should().ThrowAsync<PendingSessionAccessDeniedException>();

        _dir.AllFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Session_of_another_client_cannot_be_used()
    {
        var victim = Caller.Anonymous("1.1.1.1", "victim");
        var attacker = Caller.Anonymous("6.6.6.6", "attacker");
        var session = TestServices.Sessions(victim).IssueForCurrentCaller();

        await FluentActions.Awaiting(() => UploadAs(attacker, session, 0))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Per_session_limit_holds_under_concurrent_uploads()
    {
        var caller = Caller.Anonymous("2.2.2.2", "racer");
        var session = TestServices.Sessions(caller).IssueForCurrentCaller();

        var attempts = Enumerable.Range(0, 8).Select(async i =>
        {
            try
            {
                await UploadAs(caller, session, i);
                return "ok";
            }
            catch (SessionLimitExceededException)
            {
                return "limit";
            }
        });

        var results = await Task.WhenAll(attempts);

        results.Count(r => r == "ok").Should().Be(_options.Pending.MaxUploadsPerSession);
        results.Count(r => r == "limit").Should().Be(8 - _options.Pending.MaxUploadsPerSession);
        _dir.AllFiles().Should().HaveCount(_options.Pending.MaxUploadsPerSession, "rejected uploads leave no blobs");
    }

    [Fact]
    public async Task Duplicate_display_order_is_a_409_not_a_500()
    {
        var caller = Caller.User(Guid.NewGuid());
        var session = TestServices.Sessions(caller).IssueForCurrentCaller();
        await UploadAs(caller, session, 0);

        await FluentActions.Awaiting(() => UploadAs(caller, session, 0))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Associate_requires_the_session_owner_and_moves_files()
    {
        var owner = Caller.User(Guid.NewGuid());
        var session = TestServices.Sessions(owner).IssueForCurrentCaller();
        await UploadAs(owner, session, 0);
        await UploadAs(owner, session, 1);
        var entityId = owner.UserId!.Value;

        // Another user cannot hijack the session.
        var other = Caller.User(Guid.NewGuid());
        await using (var ctx = db.CreateContext(other))
        {
            await FluentActions.Awaiting(() => AssociateHandler(ctx, other).Handle(
                    new AssociatePendingAttachmentsCommand(session, EntityType.User, entityId, other.UserId!.Value), default))
                .Should().ThrowAsync<PendingSessionAccessDeniedException>();
        }

        await using (var ctx = db.CreateContext(owner))
        {
            var result = await AssociateHandler(ctx, owner).Handle(
                new AssociatePendingAttachmentsCommand(session, EntityType.User, entityId, owner.UserId!.Value)
                    { EnforceEntityAccessPolicy = true }, default);

            result.TotalAssociated.Should().Be(2);
            result.Metadata.Should().OnlyContain(m => m.FileUrl.StartsWith("/api/v1/attachments/"));
        }

        await using var check = db.CreateContext(owner);
        var rows = await check.Attachments.Where(a => a.EntityId == entityId).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(r => r.Status == Attachment.Domains.Attachments.Models.AttachmentStatus.Ready);
        rows.Should().OnlyContain(r => r.ContentHash != null && r.ContentHash.Length == 64,
            "the hash computed at pending upload is carried to the attachment");
        foreach (var row in rows)
            (await Storage().ExistsAsync(row.StoragePath)).Should().BeTrue();
        _dir.AllFiles().Should().OnlyContain(f => !f.Contains("temp"), "files were moved out of the temp folder");
    }

    [Fact]
    public async Task Associate_over_http_to_an_entity_without_policy_is_forbidden()
    {
        var owner = Caller.User(Guid.NewGuid());
        var session = TestServices.Sessions(owner).IssueForCurrentCaller();
        await UploadAs(owner, session, 0);

        await using var ctx = db.CreateContext(owner);
        await FluentActions.Awaiting(() => AssociateHandler(ctx, owner).Handle(
                new AssociatePendingAttachmentsCommand(session, EntityType.Company, Guid.NewGuid(), owner.UserId!.Value)
                    { EnforceEntityAccessPolicy = true }, default))
            .Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Upload_whose_db_save_fails_leaves_no_blob()
    {
        var caller = Caller.User(Guid.NewGuid());
        await using var ctx = db.CreateContext(caller);
        var options = TestOptions.Wrap(_options);
        var handler = new UploadAttachmentHandler(
            new AttachmentRepository(ctx), Storage(),
            new FileValidationService(options, new NoOpMalwareScanner(), NullLogger<FileValidationService>.Instance),
            TestServices.Access(caller), new AttachmentUrls(options), new Mock<IAttachmentBackgroundJobs>().Object,
            options, TimeProvider.System, TenantContext.SingleTenant, NullLogger<UploadAttachmentHandler>.Instance);

        var pdf = TestFiles.Pdf();
        var command = new UploadAttachmentCommand("doc.pdf", "application/pdf", pdf.Length, new MemoryStream(pdf),
            AttachmentType.Document, Description: new string('x', 5000)); // > varchar(1000): the INSERT fails

        await FluentActions.Awaiting(() => handler.Handle(command, default)).Should().ThrowAsync<Exception>();

        // Phase 1 (the Uploading row) failed, so not a single byte was written.
        _dir.AllFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Cleanup_removes_expired_rows_in_batches_and_their_files()
    {
        var caller = Caller.Anonymous("3.3.3.3", "cleanup");
        var session = TestServices.Sessions(caller).IssueForCurrentCaller();
        for (var i = 0; i < 3; i++) await UploadAs(caller, session, i);

        await using var ctx = db.CreateContext(caller);
        var handler = new CleanupExpiredPendingAttachmentsHandler(new PendingAttachmentRepository(ctx), Storage(),
            NullLogger<CleanupExpiredPendingAttachmentsHandler>.Instance);

        // Cutoff in the future = everything counts as expired; batch size 1 forces several loops.
        var result = await handler.Handle(new CleanupExpiredPendingAttachmentsCommand(DateTime.UtcNow.AddDays(30), 1), default);

        result.Cleaned.Should().BeGreaterThanOrEqualTo(3);
        await using var check = db.CreateContext(caller);
        (await check.PendingAttachments.IgnoreQueryFilters().CountAsync(p => p.SessionId == session && !p.IsDeleted))
            .Should().Be(0);
        _dir.AllFiles().Should().BeEmpty();
    }

    private AssociatePendingAttachmentsHandler AssociateHandler(AttachmentDbContext context, Caller caller)
    {
        var options = TestOptions.Wrap(_options);
        return new AssociatePendingAttachmentsHandler(
            new PendingAttachmentRepository(context),
            new AttachmentRepository(context),
            Storage(),
            TestServices.Sessions(caller),
            TestServices.Access(caller),
            new AttachmentUrls(options),
            NullLogger<AssociatePendingAttachmentsHandler>.Instance);
    }
}
