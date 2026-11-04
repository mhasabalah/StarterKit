using System.Security.Cryptography;
using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;
using Attachment.Contracts.Attachments.IntegrationEvents;
using Attachment.Domains.Attachments.Events;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Domains.Attachments.Features.DownloadAttachment;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Maintenance;
using Attachment.Infrastructure.Storage;
using Attachment.Tests.TestSupport;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Exceptions;
using Shared.Core.Messaging.Outbox;

namespace Attachment.Tests.Integration;

/// <summary>
/// Two-phase upload, Ready-only reads, async deletion, thumbnails in the background, streaming downloads and
/// paging - through the real mediator pipeline, jobs and PostgreSQL.
/// </summary>
[Collection(AttachmentHostCollection.Name)]
public sealed class AttachmentLifecycleTests : IDisposable
{
    private readonly AttachmentHostFixture _host;

    public AttachmentLifecycleTests(AttachmentHostFixture host)
    {
        _host = host;
        _host.Reset();
    }

    public void Dispose() => _host.Reset();

    private Task<UploadAttachmentResult> Upload(
        Caller caller, byte[] content, string name = "photo.png", string contentType = "image/png",
        EntityType? entityType = null, Guid? entityId = null, bool isPublic = false, bool thumbnail = true) =>
        _host.SendAsync(caller, new UploadAttachmentCommand(name, contentType, content.Length, new MemoryStream(content),
            AttachmentType.Other, entityType, entityId, IsPublic: isPublic, GenerateThumbnail: thumbnail));

    private Task<AttachmentFile?> Row(Guid id) =>
        _host.DbAsync(db => db.Attachments.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id));

    /// <summary>Inserts a row straight into the DB (any status) with a blob, for read-path and sweeper tests.</summary>
    private async Task<AttachmentFile> Seed(AttachmentStatus status, Guid owner, EntityType? type = null, Guid? entityId = null,
        bool isPublic = true, bool image = false)
    {
        var id = Guid.NewGuid();
        var (content, ext, contentType, kind) = image
            ? (TestFiles.Png(), ".png", "image/png", AttachmentType.Image)
            : (TestFiles.Pdf(), ".pdf", "application/pdf", AttachmentType.Document);
        var path = $"seed/{id:N}{ext}";
        await _host.Storage.Inner.UploadToPathAsync(new MemoryStream(content), path, contentType);

        var attachment = AttachmentFile.StartUpload(id, $"{id:N}{ext}", "seed" + ext, contentType, content.Length,
            path, $"/api/v1/attachments/{id}/download", kind, owner, type, entityId, null, isPublic);
        if (status != AttachmentStatus.Uploading)
            attachment.MarkReady(content.Length, null, null, null);
        if (status == AttachmentStatus.PendingDeletion)
            attachment.RequestDeletion(_host.Clock.GetUtcNow().UtcDateTime);

        await _host.DbAsync(async db =>
        {
            db.Attachments.Add(attachment);
            return await db.SaveChangesAsync();
        });
        return attachment;
    }

    // ----- two-phase upload -----

    [Fact]
    public async Task Upload_inserts_an_invisible_Uploading_row_before_the_blob_and_ends_Ready_with_size_and_hash()
    {
        var me = Caller.User(Guid.NewGuid());
        var png = TestFiles.Png(40, 30);
        AttachmentFile? midUpload = null;
        Exception? readWhileUploading = null;

        _host.Storage.AfterUpload = async path =>
        {
            // Phase 2 in progress: blob written, row not Ready yet.
            midUpload = await _host.DbAsync(db => db.Attachments.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(a => a.StoragePath == path));
            readWhileUploading = await Record.ExceptionAsync(() =>
                _host.InScopeAsync(sp => sp.GetRequiredService<Shared.Contracts.Mediation.ISender>().Send(new GetAttachmentByIdQuery(midUpload!.Id))));
        };

        var result = await Upload(me, png);

        midUpload.Should().NotBeNull("the row is inserted before the blob is written");
        midUpload!.Status.Should().Be(AttachmentStatus.Uploading);
        readWhileUploading.Should().BeOfType<AttachmentNotFoundException>("an Uploading row is invisible");

        var row = await Row(result.Id);
        row!.Status.Should().Be(AttachmentStatus.Ready);
        row.FileSize.Should().Be(png.Length);
        row.ContentHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(png)));
        row.Width.Should().Be(40);
        row.Height.Should().Be(30);
        _host.FileExists(row.StoragePath).Should().BeTrue();

        var dto = (await _host.SendAsync(me, new GetAttachmentByIdQuery(result.Id))).Attachment;
        dto.ContentHash.Should().Be(row.ContentHash, "the hash is exposed on the DTO");
        _host.Events.Of<AttachmentUploadedEvent>().Should().ContainSingle(e => e.Attachment.Id == result.Id,
            "the uploaded event is raised once, when the row becomes Ready");
    }

    [Fact]
    public async Task Blob_write_failure_leaves_no_row_and_no_blob()
    {
        var me = Caller.User(Guid.NewGuid());
        _host.Storage.FailUploadsAfterWrite = true;

        await FluentActions.Awaiting(() => Upload(me, TestFiles.Pdf(), "doc.pdf", "application/pdf"))
            .Should().ThrowAsync<FileStorageException>();

        var rows = await _host.DbAsync(db => db.Attachments.IgnoreQueryFilters()
            .Where(a => a.UploadedBy == me.UserId).ToListAsync());
        rows.Should().BeEmpty("the failed upload removed its Uploading row and its blob");
    }

    [Fact]
    public async Task Blob_write_failure_with_failed_cleanup_leaves_an_Uploading_row_that_the_sweeper_removes()
    {
        var me = Caller.User(Guid.NewGuid());
        _host.Storage.FailUploadsAfterWrite = true;
        _host.Storage.FailDeletes.Add("*");

        await FluentActions.Awaiting(() => Upload(me, TestFiles.Pdf(), "doc.pdf", "application/pdf"))
            .Should().ThrowAsync<FileStorageException>();

        var left = await _host.DbAsync(db => db.Attachments.IgnoreQueryFilters()
            .SingleAsync(a => a.UploadedBy == me.UserId));
        left.Status.Should().Be(AttachmentStatus.Uploading, "no Ready row after a failed blob write");
        _host.FileExists(left.StoragePath).Should().BeTrue("the blob was written before the failure");

        _host.Storage.Reset();

        // Not abandoned yet: the sweeper must leave uploads in flight alone.
        var early = await _host.InScopeAsync(sp => sp.GetRequiredService<AttachmentReconciler>()
            .ReapAbandonedUploadsAsync(_host.Clock.GetUtcNow().UtcDateTime));
        (await Row(left.Id)).Should().NotBeNull();

        _host.Clock.Advance(TimeSpan.FromMinutes(61));
        var swept = await _host.InScopeAsync(sp => sp.GetRequiredService<AttachmentReconciler>()
            .ReapAbandonedUploadsAsync(_host.Clock.GetUtcNow().UtcDateTime));

        early.Claimed.Should().Be(0);
        swept.Claimed.Should().BeGreaterThanOrEqualTo(1);
        (await Row(left.Id)).Should().BeNull("the abandoned row is removed");
        _host.FileExists(left.StoragePath).Should().BeFalse("and its blob deleted");
    }

    [Fact]
    public async Task Abandoned_upload_that_finishes_while_the_sweeper_claims_it_cannot_become_Ready()
    {
        var me = Caller.User(Guid.NewGuid());
        var attachment = await Seed(AttachmentStatus.Uploading, me.UserId!.Value);

        // The uploader still holds its tracked entity (version 0)...
        await _host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<Attachment.Data.AttachmentDbContext>();
            var tracked = await db.Attachments.IgnoreQueryFilters().SingleAsync(a => a.Id == attachment.Id);

            // ...while the sweeper claims and reaps it.
            _host.Clock.Advance(TimeSpan.FromHours(2));
            await _host.InScopeAsync(s => s.GetRequiredService<AttachmentReconciler>()
                .ReapAbandonedUploadsAsync(_host.Clock.GetUtcNow().UtcDateTime));

            tracked.MarkReady(10, null, null, null);
            await FluentActions.Awaiting(() => db.SaveChangesAsync())
                .Should().ThrowAsync<DbUpdateConcurrencyException>("the version check stops the resurrection");
        });

        (await Row(attachment.Id)).Should().BeNull();
    }

    // ----- Ready-only reads -----

    [Fact]
    public async Task Uploading_and_PendingDeletion_rows_are_invisible_to_every_read_path()
    {
        var owner = Guid.NewGuid();
        var me = Caller.User(owner);
        var ready = await Seed(AttachmentStatus.Ready, owner, EntityType.User, owner);
        var uploading = await Seed(AttachmentStatus.Uploading, owner, EntityType.User, owner);
        var deleting = await Seed(AttachmentStatus.PendingDeletion, owner, EntityType.User, owner);

        foreach (var hidden in new[] { uploading, deleting })
        {
            await FluentActions.Awaiting(() => _host.SendAsync(me, new GetAttachmentByIdQuery(hidden.Id)))
                .Should().ThrowAsync<AttachmentNotFoundException>();
            await FluentActions.Awaiting(() => _host.SendAsync(me, new DownloadAttachmentQuery(hidden.Id)))
                .Should().ThrowAsync<AttachmentNotFoundException>();
            await FluentActions.Awaiting(() => _host.SendAsync(me, new GetAttachmentReadUrlQuery(hidden.Id)))
                .Should().ThrowAsync<AttachmentNotFoundException>();
            await FluentActions.Awaiting(() => _host.SendAsync(Caller.Admin(), new DeleteAttachmentCommand(hidden.Id)))
                .Should().ThrowAsync<AttachmentNotFoundException>();
        }

        var list = await _host.SendAsync(me, new GetAttachmentsByEntityQuery(EntityType.User, owner));
        list.Attachments.Select(a => a.Id).Should().Equal(ready.Id);
        list.TotalCount.Should().Be(1);

        (await _host.SendAsync(me, new GetAttachmentByIdQuery(ready.Id))).Attachment.Id.Should().Be(ready.Id);
    }

    // ----- async deletion -----

    [Fact]
    public async Task Delete_hides_the_row_at_once_and_the_job_deletes_blobs_and_row_idempotently()
    {
        var me = Caller.User(Guid.NewGuid());
        var upload = await Upload(me, TestFiles.Png());
        await RunThumbnailJobAsync(upload.Id);
        var before = await Row(upload.Id);
        before!.ThumbnailPath.Should().NotBeNull();

        await _host.SendAsync(me, new DeleteAttachmentCommand(upload.Id));

        await FluentActions.Awaiting(() => _host.SendAsync(me, new GetAttachmentByIdQuery(upload.Id)))
            .Should().ThrowAsync<AttachmentNotFoundException>("the row is hidden immediately");
        var pending = await Row(upload.Id);
        pending!.Status.Should().Be(AttachmentStatus.PendingDeletion);
        pending.IsDeleted.Should().BeTrue();
        _host.FileExists(before.StoragePath).Should().BeTrue("blobs are deleted by the job, not the request");

        var job = _host.Jobs.Of<DeleteAttachmentFilesJob>(nameof(DeleteAttachmentFilesJob.ExecuteAsync)).Should().ContainSingle().Subject;
        var ids = (Guid[])job.Args[0];
        ids.Should().Equal(upload.Id);

        await RunDeleteJobAsync(ids);

        (await Row(upload.Id)).Should().BeNull();
        _host.FileExists(before.StoragePath).Should().BeFalse();
        _host.FileExists(before.ThumbnailPath!).Should().BeFalse("the thumbnail is deleted too");

        await FluentActions.Awaiting(() => RunDeleteJobAsync(ids)).Should().NotThrowAsync("the job is idempotent");
    }

    [Fact]
    public async Task Failed_blob_delete_is_rescheduled_with_backoff_and_retried_by_the_sweeper()
    {
        var me = Caller.User(Guid.NewGuid());
        var upload = await Upload(me, TestFiles.Pdf(), "doc.pdf", "application/pdf");
        var path = (await Row(upload.Id))!.StoragePath;

        await _host.SendAsync(me, new DeleteAttachmentCommand(upload.Id));
        _host.Storage.FailDeletes.Add(path);

        await FluentActions.Awaiting(() => RunDeleteJobAsync([upload.Id]))
            .Should().ThrowAsync<FileStorageException>("Hangfire must retry the job");

        var failed = await Row(upload.Id);
        failed!.Status.Should().Be(AttachmentStatus.PendingDeletion);
        failed.DeletionAttempts.Should().Be(1);
        failed.NextDeletionAttemptAt.Should().BeCloseTo(_host.Clock.GetUtcNow().UtcDateTime.AddMinutes(5), TimeSpan.FromSeconds(30));
        _host.FileExists(path).Should().BeTrue();

        // Not due yet: the sweeper leaves it alone.
        var notDue = await Sweep(sp => sp.RetryDueDeletionsAsync(_host.Clock.GetUtcNow().UtcDateTime));
        (await Row(upload.Id))!.DeletionAttempts.Should().Be(1);

        // Due, still failing: attempt 2, backoff doubles.
        _host.Clock.Advance(TimeSpan.FromMinutes(6));
        await Sweep(sp => sp.RetryDueDeletionsAsync(_host.Clock.GetUtcNow().UtcDateTime));
        var again = await Row(upload.Id);
        again!.DeletionAttempts.Should().Be(2);
        again.NextDeletionAttemptAt.Should().BeCloseTo(_host.Clock.GetUtcNow().UtcDateTime.AddMinutes(10), TimeSpan.FromSeconds(30));

        // Storage recovers.
        _host.Storage.Reset();
        _host.Clock.Advance(TimeSpan.FromMinutes(11));
        var done = await Sweep(sp => sp.RetryDueDeletionsAsync(_host.Clock.GetUtcNow().UtcDateTime));

        notDue.Deleted.Should().Be(0);
        done.Deleted.Should().BeGreaterThanOrEqualTo(1);
        (await Row(upload.Id)).Should().BeNull();
        _host.FileExists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_by_entity_claims_ready_and_in_flight_uploads_and_one_job_deletes_them()
    {
        var productId = Guid.NewGuid();
        var uploader = Guid.NewGuid();
        var a = await Seed(AttachmentStatus.Ready, uploader, EntityType.Product, productId);
        var b = await Seed(AttachmentStatus.Ready, uploader, EntityType.Product, productId);
        var inFlight = await Seed(AttachmentStatus.Uploading, uploader, EntityType.Product, productId);
        var other = await Seed(AttachmentStatus.Ready, uploader, EntityType.Product, Guid.NewGuid());

        var result = await _host.SendAsync(Caller.Anonymous(), new DeleteAttachmentsByEntityCommand(EntityType.Product, productId));

        result.DeletedCount.Should().Be(3);
        var list = await _host.SendAsync(Caller.Admin(), new GetAttachmentsByEntityQuery(EntityType.Product, productId));
        list.Attachments.Should().BeEmpty("rows are hidden in the same save");

        var ids = (Guid[])_host.Jobs.Of<DeleteAttachmentFilesJob>(nameof(DeleteAttachmentFilesJob.ExecuteAsync)).Single().Args[0];
        ids.Should().BeEquivalentTo([a.Id, b.Id, inFlight.Id]);

        await RunDeleteJobAsync(ids);

        foreach (var gone in new[] { a, b, inFlight })
        {
            (await Row(gone.Id)).Should().BeNull();
            _host.FileExists(gone.StoragePath).Should().BeFalse();
        }

        (await Row(other.Id)).Should().NotBeNull("another entity is untouched");
    }

    [Fact]
    public async Task New_avatar_hides_the_old_one_schedules_its_deletion_and_tells_identity()
    {
        var userId = Guid.NewGuid();
        var me = Caller.User(userId);

        var first = await _host.SendAsync(me, new UploadAttachmentCommand("a.png", "image/png", TestFiles.Png().Length,
            new MemoryStream(TestFiles.Png()), AttachmentType.Image, EntityType.User, userId)
        { EnforceEntityAccessPolicy = true });
        _host.Jobs.Clear();

        var second = await _host.SendAsync(me, new UploadAttachmentCommand("b.png", "image/png", TestFiles.Png().Length,
            new MemoryStream(TestFiles.Png()), AttachmentType.Image, EntityType.User, userId)
        { EnforceEntityAccessPolicy = true });

        (await Row(first.Id))!.Status.Should().Be(AttachmentStatus.PendingDeletion);
        (await Row(second.Id))!.Status.Should().Be(AttachmentStatus.Ready);
        ((Guid[])_host.Jobs.Of<DeleteAttachmentFilesJob>(nameof(DeleteAttachmentFilesJob.ExecuteAsync)).Single().Args[0])
            .Should().Equal(first.Id);

        var rows = await AvatarOutboxRowsFor(userId);
        rows.Should().HaveCount(2, "each avatar that became Ready raised one integration event");
        rows.Should().OnlyContain(r => r.MessageType == "urn:message:attachment.user-avatar-changed.v1");
        var payloads = rows.Select(r => System.Text.Json.JsonSerializer.Deserialize<UserAvatarChangedIntegrationEvent>(
            r.Payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!).ToList();
        payloads.Should().OnlyContain(p => p.UserId == userId);
        payloads.Select(p => p.AttachmentId).Should().BeEquivalentTo([first.Id, second.Id]);
    }

    private Task<List<TenantOutboxMessage>> AvatarOutboxRowsFor(Guid userId) =>
        _host.DbAsync(async db => (await db.Set<TenantOutboxMessage>().AsNoTracking().ToListAsync())
            .Where(m => m.Payload.Contains(userId.ToString())).ToList());

    // ----- GetUserAvatarQuery -----

    [Fact]
    public async Task GetUserAvatar_returns_the_newest_Ready_avatar()
    {
        var userId = Guid.NewGuid();
        var older = await Seed(AttachmentStatus.Ready, userId, EntityType.User, userId, image: true);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        var newer = await Seed(AttachmentStatus.Ready, userId, EntityType.User, userId, image: true);

        var result = await _host.SendAsync(Caller.Admin(), new GetUserAvatarQuery(userId));

        result.AttachmentId.Should().Be(newer.Id).And.NotBe(older.Id);
        result.AvatarUrl.Should().NotBeNullOrEmpty().And.Contain(newer.Id.ToString());
    }

    [Fact]
    public async Task GetUserAvatar_ignores_a_replaced_avatar_pending_deletion()
    {
        var userId = Guid.NewGuid();
        var current = await Seed(AttachmentStatus.Ready, userId, EntityType.User, userId, image: true);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        await Seed(AttachmentStatus.PendingDeletion, userId, EntityType.User, userId, image: true);

        var result = await _host.SendAsync(Caller.Admin(), new GetUserAvatarQuery(userId));

        result.AttachmentId.Should().Be(current.Id);
    }

    [Fact]
    public async Task GetUserAvatar_without_an_avatar_returns_nulls()
    {
        var userId = Guid.NewGuid();
        await Seed(AttachmentStatus.Ready, userId, EntityType.Product, Guid.NewGuid(), image: true);

        var result = await _host.SendAsync(Caller.Admin(), new GetUserAvatarQuery(userId));

        result.AttachmentId.Should().BeNull();
        result.AvatarUrl.Should().BeNull();
    }

    // ----- thumbnails -----

    [Fact]
    public async Task Direct_upload_queues_the_thumbnail_and_the_job_stores_it_and_publishes_its_event()
    {
        var me = Caller.User(Guid.NewGuid());

        var result = await Upload(me, TestFiles.Png(300, 200));

        result.ThumbnailUrl.Should().BeNull("thumbnails are no longer generated in the request");
        result.ThumbnailPending.Should().BeTrue();
        (await Row(result.Id))!.ThumbnailPath.Should().BeNull();
        _host.Jobs.Of<GenerateThumbnailJob>(nameof(GenerateThumbnailJob.ExecuteForAttachmentAsync))
            .Should().ContainSingle(j => (Guid)j.Args[0] == result.Id);

        await RunThumbnailJobAsync(result.Id);

        var row = await Row(result.Id);
        row!.ThumbnailPath.Should().Be(StoragePaths.ThumbnailBeside(row.StoragePath, row.Id));
        _host.FileExists(row.ThumbnailPath!).Should().BeTrue();
        row.Width.Should().Be(300, "the original dimensions are kept");
        _host.Events.Of<AttachmentThumbnailGeneratedEvent>().Should().ContainSingle(e => e.AttachmentId == result.Id,
            "the job publishes the events of its committed save (unit-of-work runner)");

        var dto = (await _host.SendAsync(me, new GetAttachmentByIdQuery(result.Id))).Attachment;
        dto.ThumbnailUrl.Should().EndWith("?thumbnail=true");
    }

    [Fact]
    public async Task Thumbnail_job_skips_rows_that_are_not_Ready()
    {
        var owner = Guid.NewGuid();
        foreach (var status in new[] { AttachmentStatus.Uploading, AttachmentStatus.PendingDeletion })
        {
            var attachment = await Seed(status, owner, image: true);
            var thumb = StoragePaths.ThumbnailBeside(attachment.StoragePath, attachment.Id);

            await RunThumbnailJobAsync(attachment.Id);

            (await Row(attachment.Id))!.ThumbnailPath.Should().BeNull();
            _host.FileExists(thumb).Should().BeFalse();
        }

        _host.Events.Of<AttachmentThumbnailGeneratedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Pending_thumbnail_job_publishes_its_event()
    {
        var caller = Caller.User(Guid.NewGuid());
        var session = await _host.InScopeAsync(sp => Task.FromResult(TestServices.Sessions(caller).IssueForCurrentCaller()));
        var png = TestFiles.Png(64, 64);
        var pending = await _host.SendAsync(caller, new UploadPendingAttachmentCommand(session, "p.png", "image/png", png.Length,
            new MemoryStream(png), AttachmentType.Image, AttachmentCategory.General));

        await _host.InScopeAsync(sp => sp.GetRequiredService<GenerateThumbnailJob>().ExecuteAsync(pending.Id));

        _host.Events.Of<PendingAttachmentThumbnailGeneratedEvent>().Should().ContainSingle(e => e.Id == pending.Id);
        var row = await _host.DbAsync(db => db.PendingAttachments.AsNoTracking().SingleAsync(p => p.Id == pending.Id));
        row.ThumbnailPath.Should().NotBeNull();
        row.ContentHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(png)));
    }

    // ----- downloads -----

    [Fact]
    public async Task Download_returns_the_provider_file_stream_without_buffering()
    {
        var me = Caller.User(Guid.NewGuid());
        var pdf = TestFiles.Pdf();
        var upload = await Upload(me, pdf, "doc.pdf", "application/pdf");

        var result = await _host.SendAsync(me, new DownloadAttachmentQuery(upload.Id));

        await using var content = result.Content!;
        content.Should().BeOfType<FileStream>("Local storage streams the file itself, never a MemoryStream copy");
        content.Position.Should().Be(0, "nothing was read ahead into a buffer");
        using var reader = new MemoryStream();
        await content.CopyToAsync(reader);
        reader.ToArray().Should().Equal(pdf);
    }

    // ----- paging -----

    [Fact]
    public async Task Attachments_of_an_entity_are_paged_in_sql_newest_first()
    {
        var owner = Guid.NewGuid();
        var me = Caller.User(owner);
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add((await Seed(AttachmentStatus.Ready, owner, EntityType.User, owner)).Id);
            _host.Clock.Advance(TimeSpan.FromSeconds(1)); // distinct CreatedAt
        }

        var newestFirst = ids.AsEnumerable().Reverse().ToList();

        var page1 = await _host.SendAsync(me, new GetAttachmentsByEntityQuery(EntityType.User, owner, PageNumber: 1, PageSize: 2));
        var page3 = await _host.SendAsync(me, new GetAttachmentsByEntityQuery(EntityType.User, owner, PageNumber: 3, PageSize: 2));
        var unpaged = await _host.SendAsync(me, new GetAttachmentsByEntityQuery(EntityType.User, owner));

        page1.Attachments.Select(a => a.Id).Should().Equal(newestFirst.Take(2));
        page1.TotalCount.Should().Be(5);
        page1.PageSize.Should().Be(2);
        page3.Attachments.Select(a => a.Id).Should().Equal(newestFirst.Skip(4));
        page3.PageNumber.Should().Be(3);
        unpaged.Attachments.Select(a => a.Id).Should().Equal(newestFirst, "no paging = first page of the max size");
        unpaged.PageSize.Should().Be(Shared.Core.Pagination.PagingOptions.MaxPageSize);

        await FluentActions.Awaiting(() => _host.SendAsync(me, new GetAttachmentsByEntityQuery(EntityType.User, owner, PageSize: 0)))
            .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Callers_without_entity_access_page_over_public_and_own_files_only()
    {
        var owner = Guid.NewGuid();
        var stranger = Caller.User(Guid.NewGuid());
        await Seed(AttachmentStatus.Ready, owner, EntityType.User, owner, isPublic: true);
        await Seed(AttachmentStatus.Ready, owner, EntityType.User, owner, isPublic: false);
        await Seed(AttachmentStatus.Ready, stranger.UserId!.Value, EntityType.User, owner, isPublic: false);

        var page = await _host.SendAsync(stranger, new GetAttachmentsByEntityQuery(EntityType.User, owner, 1, 10));

        page.TotalCount.Should().Be(2, "the public one and the stranger's own upload");
        page.Attachments.Should().OnlyContain(a => a.IsPublic || a.UploadedBy == stranger.UserId);
    }

    // ----- helpers -----

    private Task RunDeleteJobAsync(Guid[] ids) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<DeleteAttachmentFilesJob>().ExecuteAsync(ids, CancellationToken.None));

    private Task RunThumbnailJobAsync(Guid id) =>
        _host.InScopeAsync(sp => sp.GetRequiredService<GenerateThumbnailJob>().ExecuteForAttachmentAsync(id));

    private Task<T> Sweep<T>(Func<AttachmentReconciler, Task<T>> phase) =>
        _host.InScopeAsync(sp => phase(sp.GetRequiredService<AttachmentReconciler>()));
}
