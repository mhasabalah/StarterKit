using Attachment.Infrastructure.ImageProcessing;
using Attachment.Infrastructure.Storage;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Attachment.Infrastructure.BackgroundJobs;

/// <summary>
/// Creates image thumbnails outside the upload request, for pending uploads (<see cref="ExecuteAsync"/>) and for
/// Ready attachments of the direct upload path (<see cref="ExecuteForAttachmentAsync"/>).
/// <para>
/// Each run is one unit of work (<see cref="IUnitOfWorkRunner"/>): the thumbnail event of the committed save is
/// published, nothing is published when the save fails. Rows that are gone, not Ready (uploading / pending
/// deletion), deleted, expired or already associated are skipped. If saving the thumbnail fails, the
/// just-written thumbnail blob is deleted again; a concurrent change of the row (it was deleted or associated
/// meanwhile: version conflict) ends the job quietly, anything else is rethrown so Hangfire retries.
/// </para>
/// </summary>
public class GenerateThumbnailJob(
    IUnitOfWorkRunner runner,
    IImageProcessingService imageProcessing,
    IAttachmentUrls urls,
    ILogger<GenerateThumbnailJob> logger)
{
    /// <summary>Thumbnail of a pending (pre-entity) upload. Signature kept for jobs already enqueued.</summary>
    [AutomaticRetry(Attempts = 2)]
    public Task ExecuteAsync(Guid pendingAttachmentId) =>
        runner.RunAsync(async (services, ct) =>
        {
            var repository = services.GetRequiredService<IPendingAttachmentRepository>();
            var storage = services.GetRequiredService<IStorageService>();

            var pending = await repository.GetByIdAsync(pendingAttachmentId, ct);
            if (pending is null || pending.IsDeleted || pending.IsAssociated || pending.IsExpired())
            {
                logger.LogInformation("Pending attachment {Id} is gone or associated; thumbnail skipped", pendingAttachmentId);
                return;
            }

            if (pending.Type != AttachmentType.Image || !string.IsNullOrEmpty(pending.ThumbnailPath))
                return;

            var thumbnailPath = StoragePaths.ForPendingThumbnail(pending.TenantId, pending.SessionId, pending.Id);
            var thumbnail = await TryWriteThumbnailAsync(storage, pending.StoragePath, thumbnailPath, pendingAttachmentId, ct);
            if (thumbnail is not { } size)
                return;

            await SaveOrDeleteBlobAsync(storage, thumbnailPath, pendingAttachmentId, () =>
            {
                pending.SetThumbnail(thumbnailPath, $"pending-thumb:{pending.Id:N}", size.Width, size.Height);
                return repository.SaveChangesAsync(ct);
            }, ct);
        });

    /// <summary>Thumbnail of a Ready attachment (direct upload path). Keeps the original image dimensions.</summary>
    [AutomaticRetry(Attempts = 2)]
    public Task ExecuteForAttachmentAsync(Guid attachmentId) =>
        runner.RunAsync(async (services, ct) =>
        {
            var repository = services.GetRequiredService<IAttachmentRepository>();
            var storage = services.GetRequiredService<IStorageService>();

            // Read filter: only Ready, not-deleted rows are found.
            var attachment = await repository.GetByIdAsync(attachmentId, ct);
            if (attachment is null)
            {
                logger.LogInformation("Attachment {Id} is not Ready (gone, uploading or deleted); thumbnail skipped", attachmentId);
                return;
            }

            if (attachment.Type != AttachmentType.Image || !string.IsNullOrEmpty(attachment.ThumbnailPath))
                return;

            var thumbnailPath = StoragePaths.ThumbnailBeside(attachment.StoragePath, attachment.Id);
            var thumbnail = await TryWriteThumbnailAsync(storage, attachment.StoragePath, thumbnailPath, attachmentId, ct);
            if (thumbnail is not { } size)
                return;

            await SaveOrDeleteBlobAsync(storage, thumbnailPath, attachmentId, () =>
            {
                attachment.SetThumbnail(thumbnailPath, urls.Thumbnail(attachment.Id),
                    attachment.Width ?? size.Width, attachment.Height ?? size.Height);
                return repository.SaveChangesAsync(ct);
            }, ct);
        });

    /// <summary>Decodes the source (provider stream, forward reads only) and stores the JPEG thumbnail.</summary>
    private async Task<(int Width, int Height)?> TryWriteThumbnailAsync(
        IStorageService storage, string sourcePath, string thumbnailPath, Guid id, CancellationToken ct)
    {
        if (!await storage.ExistsAsync(sourcePath, ct))
        {
            logger.LogInformation("File of {Id} no longer exists; thumbnail skipped", id);
            return null;
        }

        ThumbnailResult thumbnail;
        await using (var source = await storage.OpenReadAsync(sourcePath, ct))
        {
            thumbnail = await imageProcessing.GenerateThumbnailAsync(source, ct);
        }

        await using var thumbStream = thumbnail.ThumbnailStream;
        await storage.UploadToPathAsync(thumbStream, thumbnailPath, "image/jpeg", ct);
        return (thumbnail.Width, thumbnail.Height);
    }

    private async Task SaveOrDeleteBlobAsync(
        IStorageService storage, string thumbnailPath, Guid id, Func<Task> save, CancellationToken ct)
    {
        try
        {
            await save();
        }
        catch (Exception ex)
        {
            await storage.DeleteBatchAsync([thumbnailPath], CancellationToken.None);

            if (ex.IsConcurrencyConflict())
            {
                logger.LogInformation("{Id} changed while its thumbnail was generated (deleted/associated); thumbnail discarded", id);
                return;
            }

            logger.LogWarning(ex, "Could not save the thumbnail of {Id}; thumbnail blob deleted", id);
            throw;
        }
    }
}
