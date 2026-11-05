using Hangfire;

namespace Attachment.Infrastructure.BackgroundJobs;

/// <summary>
/// Enqueues the Attachment module's background work. Enqueue failures (Hangfire storage down) are logged and
/// swallowed: the business change is already committed and the reconciliation sweeper picks up pending
/// deletions on its own; thumbnails are optional.
/// </summary>
public interface IAttachmentBackgroundJobs
{
    /// <summary>Deletes the blobs (then the rows) of attachments already marked <c>PendingDeletion</c>.</summary>
    void EnqueueFileDeletion(IReadOnlyCollection<Guid> attachmentIds);

    /// <summary>Generates the thumbnail of a Ready image attachment.</summary>
    void EnqueueThumbnail(Guid attachmentId);
}

public sealed class HangfireAttachmentBackgroundJobs(
    IBackgroundJobClient client,
    ILogger<HangfireAttachmentBackgroundJobs> logger) : IAttachmentBackgroundJobs
{
    public void EnqueueFileDeletion(IReadOnlyCollection<Guid> attachmentIds)
    {
        if (attachmentIds.Count == 0)
            return;

        var ids = attachmentIds.Distinct().ToArray();
        try
        {
            client.Enqueue<DeleteAttachmentFilesJob>(job => job.ExecuteAsync(ids, CancellationToken.None));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enqueue file deletion of {Count} attachment(s); the sweeper will retry",
                ids.Length);
        }
    }

    public void EnqueueThumbnail(Guid attachmentId)
    {
        try
        {
            client.Enqueue<GenerateThumbnailJob>(job => job.ExecuteForAttachmentAsync(attachmentId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not enqueue the thumbnail of attachment {AttachmentId}", attachmentId);
        }
    }
}
