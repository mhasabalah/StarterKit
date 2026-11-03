using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Maintenance;

public enum ReapOutcome
{
    /// <summary>Blobs deleted (or already missing) and the row hard-deleted.</summary>
    Deleted,

    /// <summary>No such row (already reaped): nothing to do. Re-runs end here (idempotent).</summary>
    AlreadyGone,

    /// <summary>The row is not pending deletion (never touched).</summary>
    NotPendingDeletion,

    /// <summary>At least one blob could not be deleted; the row stays and the next attempt is scheduled.</summary>
    Failed
}

/// <summary>
/// Finishes the deletion of ONE <c>PendingDeletion</c> attachment: deletes its blobs (file + thumbnail; missing
/// blobs count as deleted), then hard-deletes the row. On failure the attempt is counted and the next one
/// scheduled with exponential backoff (<see cref="ReconciliationOptions.DeletionRetryDelay"/>).
/// <para>
/// Must run as its own unit of work (<c>IUnitOfWorkRunner.RunAsync</c>, resolved from the runner's scope): used
/// by <c>DeleteAttachmentFilesJob</c> and the reconciliation sweeper. Two runs racing on the same row are safe:
/// both blob deletes are idempotent and the loser's row save fails the version check (log and skip).
/// </para>
/// </summary>
public sealed class AttachmentFileReaper(
    IAttachmentRepository repository,
    IStorageService storage,
    IOptions<AttachmentOptions> options,
    TimeProvider time,
    ILogger<AttachmentFileReaper> logger)
{
    public async Task<ReapOutcome> ReapAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await repository.GetForMaintenanceAsync(attachmentId, cancellationToken);
        if (attachment is null)
            return ReapOutcome.AlreadyGone;

        if (attachment.Status != AttachmentStatus.PendingDeletion)
        {
            logger.LogWarning("Attachment {AttachmentId} is {Status}, not pending deletion; files kept",
                attachmentId, attachment.Status);
            return ReapOutcome.NotPendingDeletion;
        }

        var result = await storage.DeleteBatchAsync(attachment.OwnedStoragePaths(), cancellationToken);
        if (result.AllSucceeded)
        {
            repository.Delete(attachment);
            await repository.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Attachment {AttachmentId}: files deleted, row removed", attachmentId);
            return ReapOutcome.Deleted;
        }

        var reconciliation = options.Value.Reconciliation;
        var retryAfter = reconciliation.DeletionRetryDelay(attachment.DeletionAttempts + 1);
        attachment.RecordDeletionFailure(time.GetUtcNow().UtcDateTime, retryAfter);
        await repository.SaveChangesAsync(cancellationToken);

        var level = attachment.DeletionAttempts >= reconciliation.DeletionAttemptsAlertThreshold
            ? LogLevel.Error
            : LogLevel.Warning;
        logger.Log(level,
            "Attachment {AttachmentId}: could not delete {Paths} (attempt {Attempt}); retrying in {RetryAfter}",
            attachmentId, string.Join(", ", result.Failed), attachment.DeletionAttempts, retryAfter);

        return ReapOutcome.Failed;
    }
}
