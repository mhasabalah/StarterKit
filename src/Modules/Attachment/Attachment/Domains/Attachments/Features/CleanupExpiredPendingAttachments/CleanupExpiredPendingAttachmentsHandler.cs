using Attachment.Contracts.Attachments.Commands;
using Attachment.Data.Repository.Attachments;
using Attachment.Infrastructure.Storage;

namespace Attachment.Domains.Attachments.Features.CleanupExpiredPendingAttachments;

public class CleanupExpiredPendingAttachmentsHandler(
    IPendingAttachmentRepository repository,
    IStorageService storageService,
    ILogger<CleanupExpiredPendingAttachmentsHandler> logger)
    : ICommandHandler<CleanupExpiredPendingAttachmentsCommand, CleanupExpiredPendingAttachmentsResult>
{
    public async Task<CleanupExpiredPendingAttachmentsResult> Handle(
        CleanupExpiredPendingAttachmentsCommand command,
        CancellationToken cancellationToken)
    {
        var expiredAttachments = await repository.GetExpiredUnassociatedAsync(
            command.CutoffDate,
            command.BatchSize,
            cancellationToken);

        if (!expiredAttachments.Any())
        {
            logger.LogInformation("No expired pending attachments found for cleanup");
            return new CleanupExpiredPendingAttachmentsResult(0, 0);
        }

        var filesToDelete = new List<string>();

        foreach (var attachment in expiredAttachments)
        {
            filesToDelete.Add(attachment.StoragePath);

            if (!string.IsNullOrEmpty(attachment.ThumbnailPath))
            {
                filesToDelete.Add(attachment.ThumbnailPath);
            }

            attachment.MarkAsDeleted();
        }

        // Batch delete from storage
        try
        {
            await storageService.DeleteBatchAsync(filesToDelete, cancellationToken);

            logger.LogInformation(
                "Deleted {Count} files from storage during cleanup",
                filesToDelete.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to delete some files from storage during cleanup. Continuing with database cleanup.");
            // Continue with database cleanup even if some files fail
        }

        // Save database changes
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Cleaned up {Count} expired pending attachments, deleted {FileCount} files",
            expiredAttachments.Count,
            filesToDelete.Count);

        return new CleanupExpiredPendingAttachmentsResult(
            expiredAttachments.Count,
            filesToDelete.Count);
    }
}
