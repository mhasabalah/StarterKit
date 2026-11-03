using Attachment.Contracts.Attachments.Commands;
using Attachment.Infrastructure.BackgroundJobs;

namespace Attachment.Domains.Attachments.Features.DeleteAttachment;

/// <summary>
/// Trusted (module-to-module) delete of every attachment of an entity: all live rows - Ready ones and uploads
/// still in progress - are hidden at once (<c>PendingDeletion</c>, one save) and one
/// <see cref="DeleteAttachmentFilesJob"/> deletes their files and rows. An upload racing this delete fails its
/// "Ready" save on the version check and cleans up after itself.
/// </summary>
public class DeleteAttachmentsByEntityHandler(
    IAttachmentRepository repository,
    IAttachmentBackgroundJobs backgroundJobs,
    TimeProvider time,
    ILogger<DeleteAttachmentsByEntityHandler> logger)
    : ICommandHandler<DeleteAttachmentsByEntityCommand, DeleteAttachmentsByEntityResult>
{
    public async Task<DeleteAttachmentsByEntityResult> Handle(
        DeleteAttachmentsByEntityCommand command,
        CancellationToken cancellationToken)
    {
        var attachments = await repository.GetLiveByEntityAsync(command.EntityType, command.EntityId, cancellationToken);
        if (attachments.Count == 0)
            return new DeleteAttachmentsByEntityResult(0);

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var attachment in attachments)
            attachment.RequestDeletion(now);

        await repository.SaveChangesAsync(cancellationToken);

        backgroundJobs.EnqueueFileDeletion(attachments.Select(a => a.Id).ToList());

        logger.LogInformation("{Count} attachment(s) of {EntityType} {EntityId} marked for deletion",
            attachments.Count, command.EntityType, command.EntityId);

        return new DeleteAttachmentsByEntityResult(attachments.Count);
    }
}
