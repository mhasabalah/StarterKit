using Attachment.Contracts.Attachments.Commands;
using Attachment.Infrastructure.Storage;

namespace Attachment.Domains.Attachments.Features.DeleteAttachment;

public class DeleteAttachmentsByEntityHandler(
    IAttachmentRepository repository,
    IStorageService storageService,
    ILogger<DeleteAttachmentsByEntityHandler> logger)
    : ICommandHandler<DeleteAttachmentsByEntityCommand, DeleteAttachmentsByEntityResult>
{
    public async Task<DeleteAttachmentsByEntityResult> Handle(
        DeleteAttachmentsByEntityCommand command,
        CancellationToken cancellationToken)
    {
        var attachments = await repository.GetByEntityAsync(
            command.EntityType,
            command.EntityId,
            cancellationToken);

        if (attachments.Count == 0)
        {
            logger.LogDebug(
                "No attachments found for {EntityType} {EntityId} — nothing to delete",
                command.EntityType,
                command.EntityId);

            return new DeleteAttachmentsByEntityResult(0);
        }

        // Collect all storage paths (file + thumbnail) for batch deletion
        var storagePaths = attachments
            .SelectMany(a => new[] { a.StoragePath, a.ThumbnailPath }
                .Where(p => !string.IsNullOrEmpty(p))!)
            .ToList();

        try
        {
            await storageService.DeleteBatchAsync(storagePaths, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to delete physical files for {EntityType} {EntityId}. " +
                "Paths attempted: {Paths}",
                command.EntityType,
                command.EntityId,
                string.Join(", ", storagePaths));

            // Re-throw so the DB records are NOT deleted — avoids dangling DB rows
            // with no corresponding storage files AND lost storage files with orphan DB rows.
            throw;
        }

        // Hard delete DB records after storage deletion succeeded
        var deletedCount = 0;
        foreach (var attachment in attachments)
        {
            var deleted = await repository.DeleteAsync(attachment.Id, cancellationToken);
            if (deleted) deletedCount++;
        }

        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Deleted {Count} attachment(s) for {EntityType} {EntityId}",
            deletedCount,
            command.EntityType,
            command.EntityId);

        return new DeleteAttachmentsByEntityResult(deletedCount);
    }
}
