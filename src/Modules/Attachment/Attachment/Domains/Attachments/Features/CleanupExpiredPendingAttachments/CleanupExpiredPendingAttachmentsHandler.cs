using Attachment.Contracts.Attachments.Commands;
using Attachment.Infrastructure.Storage;

namespace Attachment.Domains.Attachments.Features.CleanupExpiredPendingAttachments;

/// <summary>
/// Deletes expired, never-associated pending uploads. Loops batches until nothing is left. A row is only
/// marked deleted when its file (and thumbnail) were deleted from storage; failed rows are skipped for the
/// rest of this run and retried by the next run.
/// </summary>
public class CleanupExpiredPendingAttachmentsHandler(
    IPendingAttachmentRepository repository,
    IStorageService storageService,
    ILogger<CleanupExpiredPendingAttachmentsHandler> logger)
    : ICommandHandler<CleanupExpiredPendingAttachmentsCommand, CleanupExpiredPendingAttachmentsResult>
{
    /// <summary>Safety stop so one run cannot spin forever.</summary>
    private const int MaxBatchesPerRun = 10_000;

    public async Task<CleanupExpiredPendingAttachmentsResult> Handle(
        CleanupExpiredPendingAttachmentsCommand command,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(1, command.BatchSize);
        var failedIds = new HashSet<Guid>();
        var cleaned = 0;
        var filesDeleted = 0;

        for (var batchNo = 0; batchNo < MaxBatchesPerRun; batchNo++)
        {
            var batch = await repository.GetExpiredUnassociatedAsync(
                command.CutoffDate, batchSize, failedIds, cancellationToken);

            if (batch.Count == 0)
                break;

            var paths = batch
                .SelectMany(p => new[] { p.StoragePath, p.ThumbnailPath })
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => p!)
                .ToList();

            var result = await storageService.DeleteBatchAsync(paths, cancellationToken);

            foreach (var pending in batch)
            {
                var ok = result.Succeeded(pending.StoragePath) &&
                         (pending.ThumbnailPath is null || result.Succeeded(pending.ThumbnailPath));

                if (ok)
                {
                    pending.MarkAsDeleted();
                    cleaned++;
                    filesDeleted += pending.ThumbnailPath is null ? 1 : 2;
                }
                else
                {
                    failedIds.Add(pending.Id);
                }
            }

            await repository.SaveChangesAsync(cancellationToken);
        }

        if (failedIds.Count > 0)
        {
            logger.LogWarning(
                "Pending cleanup: {Failed} attachment(s) kept because their files could not be deleted; retrying next run",
                failedIds.Count);
        }

        logger.LogInformation("Pending cleanup: {Cleaned} attachment(s) removed, {Files} file(s) deleted",
            cleaned, filesDeleted);

        return new CleanupExpiredPendingAttachmentsResult(cleaned, filesDeleted);
    }
}
