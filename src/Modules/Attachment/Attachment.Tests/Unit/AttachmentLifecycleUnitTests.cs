using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.IntegrationEvents;
using Attachment.Contracts.Authorization;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Events;
using Attachment.Domains.Attachments.Features.DeleteAttachment;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Maintenance;
using Attachment.Infrastructure.Storage;
using Attachment.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Exceptions;

namespace Attachment.Tests.Unit;

public sealed class AttachmentLifecycleUnitTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static AttachmentFile Uploading(AttachmentType type = AttachmentType.Document) =>
        AttachmentFile.StartUpload(Guid.NewGuid(), "f.pdf", "f.pdf", "application/pdf", 10, "2026/11/02/f.pdf", "/u",
            type, Guid.NewGuid());

    // ----- domain -----

    [Fact]
    public void StartUpload_is_Uploading_not_ready_and_raises_no_event()
    {
        var attachment = Uploading();

        attachment.Status.Should().Be(AttachmentStatus.Uploading);
        attachment.IsReady.Should().BeFalse();
        attachment.DomainEvents.Should().BeEmpty("nothing happened yet from the outside world's view");
    }

    [Fact]
    public void MarkReady_records_size_hash_and_dimensions_and_raises_the_uploaded_event()
    {
        var attachment = Uploading();

        attachment.MarkReady(1234, Hash, 640, 480);

        attachment.Status.Should().Be(AttachmentStatus.Ready);
        attachment.IsReady.Should().BeTrue();
        attachment.FileSize.Should().Be(1234);
        attachment.ContentHash.Should().Be(Hash);
        (attachment.Width, attachment.Height).Should().Be((640, 480));
        attachment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<AttachmentUploadedEvent>();
    }

    [Fact]
    public void MarkReady_is_rejected_unless_Uploading()
    {
        var ready = Uploading();
        ready.MarkReady(10, null, null, null);
        var claimed = Uploading();
        claimed.RequestDeletion(Now);

        FluentActions.Invoking(() => ready.MarkReady(10, null, null, null)).Should().Throw<ConflictException>();
        FluentActions.Invoking(() => claimed.MarkReady(10, null, null, null))
            .Should().Throw<ConflictException>("a row claimed by the sweeper or a delete must not come back");
    }

    [Theory]
    [InlineData("ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789")] // upper case
    [InlineData("0123")]                                                             // too short
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")] // not hex
    public void MarkReady_rejects_a_malformed_hash(string hash)
    {
        FluentActions.Invoking(() => Uploading().MarkReady(10, hash, null, null)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_is_Ready_at_once_for_already_stored_blobs()
    {
        var attachment = AttachmentFile.Create(Guid.NewGuid(), "f", "f", "application/pdf", 10, "a/b.pdf", "/u",
            AttachmentType.Document, Guid.NewGuid(), contentHash: Hash);

        attachment.IsReady.Should().BeTrue();
        attachment.ContentHash.Should().Be(Hash);
    }

    [Fact]
    public void RequestDeletion_hides_the_row_schedules_it_and_is_idempotent()
    {
        var attachment = Uploading();
        attachment.MarkReady(10, null, null, null);
        attachment.ClearDomainEvents();

        attachment.RequestDeletion(Now).Should().BeTrue();
        attachment.RequestDeletion(Now.AddHours(1)).Should().BeFalse();

        attachment.Status.Should().Be(AttachmentStatus.PendingDeletion);
        attachment.IsDeleted.Should().BeTrue();
        attachment.IsReady.Should().BeFalse();
        attachment.DeletionRequestedAt.Should().Be(Now);
        attachment.NextDeletionAttemptAt.Should().Be(Now);
        attachment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<AttachmentDeletedEvent>();
        // Other modules learn about it through the outbox, once.
        attachment.IntegrationEvents.OfType<AttachmentDeletedIntegrationEvent>().Should().ContainSingle()
            .Which.AttachmentId.Should().Be(attachment.Id);
    }

    [Fact]
    public void RecordDeletionFailure_counts_attempts_and_requires_PendingDeletion()
    {
        var attachment = Uploading();
        FluentActions.Invoking(() => attachment.RecordDeletionFailure(Now, TimeSpan.FromMinutes(5)))
            .Should().Throw<InvalidOperationException>();

        attachment.RequestDeletion(Now);
        attachment.RecordDeletionFailure(Now, TimeSpan.FromMinutes(5));
        attachment.RecordDeletionFailure(Now, TimeSpan.FromMinutes(10));

        attachment.DeletionAttempts.Should().Be(2);
        attachment.NextDeletionAttemptAt.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public void Owned_storage_paths_include_the_thumbnail_when_present()
    {
        var attachment = Uploading(AttachmentType.Image);
        attachment.OwnedStoragePaths().Should().Equal("2026/11/02/f.pdf");

        attachment.SetThumbnail("2026/11/02/thumb.jpg", "/t", 10, 10);
        attachment.OwnedStoragePaths().Should().Equal("2026/11/02/f.pdf", "2026/11/02/thumb.jpg");
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(9, 1280)]
    [InlineData(10, 1440)]  // capped
    [InlineData(500, 1440)] // no overflow
    public void Deletion_retry_delay_doubles_and_is_capped(int attempts, int expectedMinutes)
    {
        new ReconciliationOptions().DeletionRetryDelay(attempts).Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Theory]
    [InlineData("2026/11/02/abc.png", "2026/11/02/thumb_")]
    [InlineData("products/p1/abc.png", "products/p1/thumb_")]
    [InlineData("abc.png", "thumb_")]
    public void Thumbnail_is_stored_next_to_the_original(string original, string expectedPrefix)
    {
        var id = Guid.NewGuid();
        StoragePaths.ThumbnailBeside(original, id).Should().Be($"{expectedPrefix}{id:N}.jpg");
    }

    [Fact]
    public void Options_validator_rejects_bad_reconciliation_settings()
    {
        var options = TestOptions.Create(o =>
        {
            o.Reconciliation.Cron = "";
            o.Reconciliation.BatchSize = 0;
            o.Reconciliation.UploadingTimeoutMinutes = 1;
            o.Reconciliation.DeletionRetryMaxDelayMinutes = 1;
            o.Reconciliation.StorageScan.SafetyWindowHours = 0;
        });

        var result = new AttachmentOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(5);
        new AttachmentOptionsValidator().Validate(null, TestOptions.Create()).Succeeded.Should().BeTrue();
    }

    // ----- delete handlers: hide now, delete files later -----

    [Fact]
    public async Task Delete_marks_pending_deletion_saves_and_enqueues_without_touching_storage()
    {
        var owner = Guid.NewGuid();
        var attachment = AttachmentFile.Create(Guid.NewGuid(), "f", "f", "application/pdf", 10, "a/b.pdf", "/u",
            AttachmentType.Document, owner);
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.GetByIdAsync(attachment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attachment);
        var jobs = new Mock<IAttachmentBackgroundJobs>();

        var handler = new DeleteAttachmentHandler(repo.Object, TestServices.Access(Caller.User(owner)), jobs.Object,
            TimeProvider.System, NullLogger<DeleteAttachmentHandler>.Instance);

        var result = await handler.Handle(new DeleteAttachmentCommand(attachment.Id), default);

        result.Success.Should().BeTrue();
        attachment.Status.Should().Be(AttachmentStatus.PendingDeletion);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        jobs.Verify(j => j.EnqueueFileDeletion(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == attachment.Id)), Times.Once);
    }

    [Fact]
    public async Task Delete_by_someone_else_is_forbidden_and_changes_nothing()
    {
        var attachment = AttachmentFile.Create(Guid.NewGuid(), "f", "f", "application/pdf", 10, "a/b.pdf", "/u",
            AttachmentType.Document, Guid.NewGuid());
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.GetByIdAsync(attachment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attachment);
        var jobs = new Mock<IAttachmentBackgroundJobs>();

        var handler = new DeleteAttachmentHandler(repo.Object, TestServices.Access(Caller.User(Guid.NewGuid())), jobs.Object,
            TimeProvider.System, NullLogger<DeleteAttachmentHandler>.Instance);

        await FluentActions.Awaiting(() => handler.Handle(new DeleteAttachmentCommand(attachment.Id), default))
            .Should().ThrowAsync<ForbiddenException>();

        attachment.Status.Should().Be(AttachmentStatus.Ready);
        jobs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Delete_by_entity_without_attachments_saves_and_enqueues_nothing()
    {
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.GetLiveByEntityAsync(EntityType.Product, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var jobs = new Mock<IAttachmentBackgroundJobs>();

        var result = await new DeleteAttachmentsByEntityHandler(repo.Object, jobs.Object, TimeProvider.System,
                NullLogger<DeleteAttachmentsByEntityHandler>.Instance)
            .Handle(new DeleteAttachmentsByEntityCommand(EntityType.Product, Guid.NewGuid()), default);

        result.DeletedCount.Should().Be(0);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        jobs.VerifyNoOtherCalls();
    }

    // ----- reaper -----

    [Fact]
    public async Task Reaper_never_touches_rows_that_are_not_pending_deletion()
    {
        var ready = AttachmentFile.Create(Guid.NewGuid(), "f", "f", "application/pdf", 10, "a/b.pdf", "/u",
            AttachmentType.Document, Guid.NewGuid());
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.GetForMaintenanceAsync(ready.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ready);
        var storage = new Mock<IStorageService>();

        var reaper = new AttachmentFileReaper(repo.Object, storage.Object, TestOptions.Wrap(TestOptions.Create()),
            TimeProvider.System, NullLogger<AttachmentFileReaper>.Instance);

        (await reaper.ReapAsync(ready.Id, default)).Should().Be(ReapOutcome.NotPendingDeletion);
        (await reaper.ReapAsync(Guid.NewGuid(), default)).Should().Be(ReapOutcome.AlreadyGone);
        storage.VerifyNoOtherCalls();
        repo.Verify(r => r.Delete(It.IsAny<AttachmentFile>()), Times.Never);
    }

    // ----- access decisions are made once per request -----

    [Fact]
    public async Task Entity_policy_is_asked_once_per_request_and_operation()
    {
        var policy = new CountingPolicy();
        var access = TestServices.Access(Caller.User(Guid.NewGuid()),
            services => services.AddKeyedSingleton<IAttachmentEntityAccessPolicy>(EntityType.Company, policy));
        var entity = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
        {
            await access.CanReadEntityAsync(EntityType.Company, entity);
            await access.CanAttachToEntityAsync(EntityType.Company, entity);
        }

        policy.ReadCalls.Should().Be(1);
        policy.AttachCalls.Should().Be(1);
    }

    private sealed class CountingPolicy : IAttachmentEntityAccessPolicy
    {
        public int ReadCalls { get; private set; }
        public int AttachCalls { get; private set; }

        public Task<bool> CanAttachAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken cancellationToken = default)
        {
            AttachCalls++;
            return Task.FromResult(true);
        }

        public Task<bool> CanReadAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            return Task.FromResult(true);
        }
    }
}
