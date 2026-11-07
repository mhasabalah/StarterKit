using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Data;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Features.AssociatePendingAttachments;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Attachment.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Attachment.Tests.Integration;

/// <summary>
/// Associating a pending image while its thumbnail job saves the row, on real PostgreSQL. The job's commit (thumbnail
/// path + version) lands between the associate's read and its save, which is a REAL optimistic-concurrency conflict.
/// Associate used to answer 409 "modified by another request" (seen as a flaky E2E in CI). It must now succeed, carry
/// the late thumbnail to the entity folder and leave nothing in the pending area. A row that was really changed by
/// someone else (deleted) must still fail, with the file moved back.
/// </summary>
[Collection(AttachmentPostgresCollection.Name)]
public sealed class PendingAssociateRaceTests(AttachmentPostgresFixture db) : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly AttachmentOptions _options = TestOptions.Create();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Associate_wins_against_a_thumbnail_saved_mid_associate_and_moves_that_thumbnail_too()
    {
        var (caller, sessionId, id, original, thumbnail, storage) = await SeedPendingImageAsync();

        async Task ThumbnailJobCommitsAsync()
        {
            await storage.UploadToPathAsync(new MemoryStream([1, 2, 3]), thumbnail, "image/jpeg");
            await using var job = db.CreateContext(caller);
            var row = await job.PendingAttachments.SingleAsync(p => p.Id == id);
            row.SetThumbnail(thumbnail, "pending-thumb", 20, 20);
            await job.SaveChangesAsync();
        }

        await using var context = db.CreateContext(caller);
        var (handler, raced) = Handler(context, caller, storage, ThumbnailJobCommitsAsync);

        var result = await handler.Handle(
            new AssociatePendingAttachmentsCommand(sessionId, EntityType.User, caller.UserId!.Value, caller.UserId!.Value),
            CancellationToken.None);

        raced.Conflicted.Should().BeTrue("the race really happened");
        result.TotalAssociated.Should().Be(1);

        await using var check = db.CreateContext(caller);
        (await check.PendingAttachments.IgnoreQueryFilters().SingleAsync(p => p.Id == id)).IsAssociated.Should().BeTrue();
        var attachment = await check.Attachments.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
        attachment.ThumbnailPath.Should().NotBeNullOrEmpty("the thumbnail committed mid-associate is carried over");
        (await storage.ExistsAsync(attachment.StoragePath)).Should().BeTrue();
        (await storage.ExistsAsync(attachment.ThumbnailPath!)).Should().BeTrue();
        (await storage.ExistsAsync(original)).Should().BeFalse("the original left the pending area");
        (await storage.ExistsAsync(thumbnail)).Should().BeFalse("the late thumbnail left the pending area too");
    }

    [Fact]
    public async Task A_pending_row_deleted_mid_associate_still_fails_and_the_file_is_moved_back()
    {
        var (caller, sessionId, id, original, _, storage) = await SeedPendingImageAsync();

        async Task DeletedMeanwhileAsync()
        {
            await using var other = db.CreateContext(caller);
            var row = await other.PendingAttachments.SingleAsync(p => p.Id == id);
            row.MarkAsDeleted();
            await other.SaveChangesAsync();
        }

        await using var context = db.CreateContext(caller);
        var (handler, raced) = Handler(context, caller, storage, DeletedMeanwhileAsync);

        var act = () => handler.Handle(
            new AssociatePendingAttachmentsCommand(sessionId, EntityType.User, caller.UserId!.Value, caller.UserId!.Value),
            CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        raced.Conflicted.Should().BeTrue();
        (await storage.ExistsAsync(original)).Should().BeTrue("a failed associate moves the file back");
        await using var check = db.CreateContext(caller);
        (await check.Attachments.IgnoreQueryFilters().AnyAsync(a => a.Id == id)).Should().BeFalse();
    }

    private async Task<(Caller Caller, Guid SessionId, Guid Id, string Original, string Thumbnail, LocalFileStorageService Storage)>
        SeedPendingImageAsync()
    {
        var caller = Caller.User(Guid.NewGuid());
        var storage = TestServices.LocalStorage(_dir.Path, _options);
        var sessionId = TestServices.Sessions(caller).IssueForCurrentCaller();
        var id = Guid.NewGuid();
        var original = $"temp/{sessionId:N}/{id:N}.png";
        var thumbnail = $"temp/{sessionId:N}/{id:N}_thumb.jpg";

        await storage.UploadToPathAsync(new MemoryStream(TestFiles.Png()), original, "image/png");
        await using var seed = db.CreateContext(caller);
        seed.PendingAttachments.Add(PendingAttachment.Create(id, sessionId, $"{id:N}.png", "me.png", "image/png", 10,
            original, "pending", AttachmentType.Image, AttachmentCategory.General, 0, false, 1));
        await seed.SaveChangesAsync();
        return (caller, sessionId, id, original, thumbnail, storage);
    }

    private (AssociatePendingAttachmentsHandler Handler, RaceProbe Probe) Handler(
        AttachmentDbContext context, Caller caller, IStorageService storage, Func<Task> beforeFirstSave)
    {
        var probe = new RaceProbe();
        var real = new AttachmentRepository(context);
        // The real repository, with another writer's commit injected before the FIRST save.
        var attachments = new Mock<IAttachmentRepository>(MockBehavior.Strict);
        attachments.Setup(r => r.AddAsync(It.IsAny<AttachmentFile>(), It.IsAny<CancellationToken>()))
            .Returns((AttachmentFile a, CancellationToken ct) => real.AddAsync(a, ct));
        attachments.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                if (!probe.Raced)
                {
                    probe.Raced = true;
                    await beforeFirstSave();
                }

                try
                {
                    return await real.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    probe.Conflicted = true;
                    throw;
                }
            });

        var handler = new AssociatePendingAttachmentsHandler(
            new PendingAttachmentRepository(context),
            attachments.Object,
            storage,
            TestServices.Sessions(caller),
            TestServices.Access(caller),
            new AttachmentUrls(TestOptions.Wrap(_options)),
            NullLogger<AssociatePendingAttachmentsHandler>.Instance);
        return (handler, probe);
    }

    private sealed class RaceProbe
    {
        public bool Raced { get; set; }

        public bool Conflicted { get; set; }
    }
}
