using Shared.Core.MultiTenancy;
using System.Security.Cryptography;
using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Domains.Attachments.Features.CleanupExpiredPendingAttachments;
using Attachment.Domains.Attachments.Features.UploadAttachment;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Storage;
using Attachment.Infrastructure.Validation;
using Attachment.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Exceptions;

namespace Attachment.Tests.Unit;

/// <summary>
/// DB/blob consistency of the two-phase upload (row first, then blob, then Ready) and of the pending cleanup.
/// </summary>
public sealed class ConsistencyTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly Mock<IAttachmentBackgroundJobs> _jobs = new();

    public void Dispose() => _dir.Dispose();

    private UploadAttachmentHandler UploadHandler(IAttachmentRepository repo, Caller caller, IStorageService? storage = null)
    {
        var options = TestOptions.Wrap(TestOptions.Create());
        return new UploadAttachmentHandler(
            repo, storage ?? TestServices.LocalStorage(_dir.Path),
            new FileValidationService(options, new NoOpMalwareScanner(),
                new Attachment.Infrastructure.ImageProcessing.ImageProcessingService(options, NullLogger<Attachment.Infrastructure.ImageProcessing.ImageProcessingService>.Instance), NullLogger<FileValidationService>.Instance),
            TestServices.Access(caller),
            new AttachmentUrls(options),
            _jobs.Object,
            options,
            TimeProvider.System,
            TenantContext.SingleTenant,
            NullLogger<UploadAttachmentHandler>.Instance);
    }

    private static UploadAttachmentCommand PngUpload(EntityType? type = null, Guid? entityId = null, byte[]? png = null)
    {
        png ??= TestFiles.Png(64, 64);
        return new UploadAttachmentCommand("photo.png", "image/png", png.Length, new MemoryStream(png),
            AttachmentType.Image, type, entityId);
    }

    [Fact]
    public async Task When_the_Uploading_row_cannot_be_saved_nothing_is_written_to_storage()
    {
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => UploadHandler(repo.Object, Caller.User(Guid.NewGuid())).Handle(PngUpload(), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _dir.AllFiles().Should().BeEmpty("the row is saved before any blob is written");
    }

    [Fact]
    public async Task When_the_Ready_save_fails_the_blob_is_deleted_and_the_row_removed()
    {
        AttachmentFile? row = null;
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<AttachmentFile>(), It.IsAny<CancellationToken>()))
            .Callback<AttachmentFile, CancellationToken>((a, _) => row = a);
        repo.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)                                         // phase 1: Uploading row
            .ThrowsAsync(new InvalidOperationException("db down"))  // phase 2: Ready
            .ReturnsAsync(1);                                        // compensation: row removed

        var act = () => UploadHandler(repo.Object, Caller.User(Guid.NewGuid())).Handle(PngUpload(), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _dir.AllFiles().Should().BeEmpty("the written blob is deleted again");
        repo.Verify(r => r.Delete(row!), Times.Once, "the Uploading row is removed too");
        _jobs.Verify(j => j.EnqueueThumbnail(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task When_the_blob_write_fails_and_its_cleanup_too_the_row_stays_Uploading_for_the_sweeper()
    {
        AttachmentFile? row = null;
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<AttachmentFile>(), It.IsAny<CancellationToken>()))
            .Callback<AttachmentFile, CancellationToken>((a, _) => row = a);

        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.UploadToPathAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileStorageException("network"));
        storage.Setup(s => s.DeleteBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> p, CancellationToken _) => new BatchDeleteResult(p.ToList()));

        var act = () => UploadHandler(repo.Object, Caller.User(Guid.NewGuid()), storage.Object).Handle(PngUpload(), default);

        await act.Should().ThrowAsync<FileStorageException>();
        row!.Status.Should().Be(AttachmentStatus.Uploading, "never Ready without a stored blob");
        repo.Verify(r => r.Delete(It.IsAny<AttachmentFile>()), Times.Never,
            "the row is kept while its blob may exist, so the sweeper can find both");
    }

    [Fact]
    public async Task Successful_upload_stores_server_determined_name_type_hash_and_queues_the_thumbnail()
    {
        AttachmentFile? saved = null;
        var repo = new Mock<IAttachmentRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<AttachmentFile>(), It.IsAny<CancellationToken>()))
            .Callback<AttachmentFile, CancellationToken>((a, _) => saved = a);

        var png = TestFiles.Png();
        var command = new UploadAttachmentCommand("../../evil.png", "application/octet-stream", png.Length,
            new MemoryStream(png), AttachmentType.Other);

        var result = await UploadHandler(repo.Object, Caller.User(Guid.NewGuid())).Handle(command, default);

        saved!.Status.Should().Be(AttachmentStatus.Ready);
        saved.ContentType.Should().Be("image/png");
        saved.Type.Should().Be(AttachmentType.Image);
        saved.OriginalFileName.Should().Be("evil.png");
        saved.StoragePath.Should().EndWith($"{saved.Id:N}.png").And.NotContain("evil");
        saved.FileSize.Should().Be(png.Length);
        saved.ContentHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(png)));
        result.FileUrl.Should().Be($"/api/v1/attachments/{saved.Id}/download");
        result.ThumbnailUrl.Should().BeNull("the thumbnail is generated by a background job");
        result.ThumbnailPending.Should().BeTrue();
        _dir.AllFiles().Should().ContainSingle("only the original; no thumbnail in the request");
        _jobs.Verify(j => j.EnqueueThumbnail(saved.Id), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2), "Uploading, then Ready");
    }

    [Fact]
    public async Task Documents_and_opted_out_images_queue_no_thumbnail()
    {
        var repo = new Mock<IAttachmentRepository>();
        var pdf = TestFiles.Pdf();

        var doc = await UploadHandler(repo.Object, Caller.User(Guid.NewGuid())).Handle(
            new UploadAttachmentCommand("a.pdf", "application/pdf", pdf.Length, new MemoryStream(pdf), AttachmentType.Document),
            default);
        var image = await UploadHandler(repo.Object, Caller.User(Guid.NewGuid())).Handle(
            PngUpload() with { GenerateThumbnail = false }, default);

        doc.ThumbnailPending.Should().BeFalse();
        image.ThumbnailPending.Should().BeFalse();
        _jobs.Verify(j => j.EnqueueThumbnail(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Http_upload_to_another_users_entity_is_forbidden_and_writes_nothing()
    {
        var repo = new Mock<IAttachmentRepository>();
        var command = PngUpload(EntityType.User, Guid.NewGuid()) with { EnforceEntityAccessPolicy = true };

        await FluentActions.Awaiting(() => UploadHandler(repo.Object, Caller.User(Guid.NewGuid())).Handle(command, default))
            .Should().ThrowAsync<ForbiddenException>();

        _dir.AllFiles().Should().BeEmpty();
        repo.Verify(r => r.AddAsync(It.IsAny<AttachmentFile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Http_upload_of_own_avatar_is_allowed()
    {
        var me = Guid.NewGuid();
        var command = PngUpload(EntityType.User, me) with { EnforceEntityAccessPolicy = true };

        var result = await UploadHandler(new Mock<IAttachmentRepository>().Object, Caller.User(me)).Handle(command, default);

        result.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Cleanup_keeps_rows_whose_blob_delete_failed_and_loops_all_batches()
    {
        var rows = Enumerable.Range(0, 5).Select(_ => Pending()).ToList();
        var failing = rows[2];

        var repo = new Mock<IPendingAttachmentRepository>();
        repo.Setup(r => r.GetExpiredUnassociatedAsync(It.IsAny<DateTime>(), 2, It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime _, int size, IReadOnlyCollection<Guid> exclude, CancellationToken _) =>
                rows.Where(r => !r.IsDeleted && !exclude.Contains(r.Id)).Take(size).ToList());

        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.DeleteBatchAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> paths, CancellationToken _) =>
                paths.Contains(failing.StoragePath)
                    ? new BatchDeleteResult([failing.StoragePath])
                    : BatchDeleteResult.Success);

        var handler = new CleanupExpiredPendingAttachmentsHandler(repo.Object, storage.Object,
            NullLogger<CleanupExpiredPendingAttachmentsHandler>.Instance);

        var result = await handler.Handle(new CleanupExpiredPendingAttachmentsCommand(DateTime.UtcNow.AddDays(10), 2), default);

        result.Cleaned.Should().Be(4, "5 rows in batches of 2: all but the failing one");
        failing.IsDeleted.Should().BeFalse("its file still exists, retry on the next run");
        rows.Where(r => r != failing).Should().OnlyContain(r => r.IsDeleted);
    }

    private static PendingAttachment Pending()
    {
        var id = Guid.NewGuid();
        return PendingAttachment.Create(id, Guid.NewGuid(), "f.png", "f.png", "image/png", 10,
            $"temp/s/{id:N}.png", "pending", AttachmentType.Image, AttachmentCategory.General, 0, false, 1);
    }
}
