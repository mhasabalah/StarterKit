using Attachment.Contracts.Attachments.Commands;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Storage;
using Attachment.Resources;
using Microsoft.Extensions.Localization;

namespace Attachment.Domains.Attachments.Features.DeletePendingAttachment;

public class DeletePendingAttachmentCommandValidator : AbstractValidator<DeletePendingAttachmentCommand>
{
    public DeletePendingAttachmentCommandValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage(_ => localizer["Validation.PendingAttachmentId.Required"]);

        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage(_ => localizer["Validation.SessionId.Required"]);
    }
}

public class DeletePendingAttachmentHandler(
    IPendingAttachmentRepository repository,
    IStorageService storageService,
    ILogger<DeletePendingAttachmentHandler> logger)
    : ICommandHandler<DeletePendingAttachmentCommand, bool>
{
    public async Task<bool> Handle(
        DeletePendingAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var pendingAttachment = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (pendingAttachment == null)
        {
            throw new PendingAttachmentNotFoundException(command.Id);
        }

        // Verify session ownership
        if (!pendingAttachment.BelongsToSession(command.SessionId))
        {
            throw new UnauthorizedAccessException(
                $"Pending attachment {command.Id} does not belong to session {command.SessionId}");
        }

        // Delete from blob storage
        var filesToDelete = new List<string> { pendingAttachment.StoragePath };

        if (!string.IsNullOrEmpty(pendingAttachment.ThumbnailPath))
        {
            filesToDelete.Add(pendingAttachment.ThumbnailPath);
        }

        try
        {
            await storageService.DeleteBatchAsync(filesToDelete, cancellationToken);

            logger.LogInformation(
                "Deleted {Count} files from storage for pending attachment {Id}",
                filesToDelete.Count,
                command.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to delete files from storage for pending attachment {Id}",
                command.Id);
            // Continue with database deletion even if storage delete fails
        }

        // Mark as deleted in database
        pendingAttachment.MarkAsDeleted();
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pending attachment deleted: Id={Id}, SessionId={SessionId}",
            command.Id,
            command.SessionId);

        return true;
    }
}
