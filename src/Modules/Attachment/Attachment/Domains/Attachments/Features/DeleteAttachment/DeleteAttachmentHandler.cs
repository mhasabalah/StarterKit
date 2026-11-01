using Attachment.Contracts.Attachments.Commands;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.Storage;
using Attachment.Resources;
using Microsoft.Extensions.Localization;

namespace Attachment.Domains.Attachments.Features.DeleteAttachment;

public class DeleteAttachmentCommandValidator : AbstractValidator<DeleteAttachmentCommand>
{
    public DeleteAttachmentCommandValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage(_ => localizer["Validation.AttachmentId.Required"]);
    }
}

public class DeleteAttachmentHandler(
    IAttachmentRepository repository,
    IStorageService storageService,
    IAttachmentAuthorizationService authorizationService,
    ILogger<DeleteAttachmentHandler> logger)
    : ICommandHandler<DeleteAttachmentCommand, DeleteAttachmentResult>
{
    public async Task<DeleteAttachmentResult> Handle(
        DeleteAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var attachment = await repository.GetByIdAsync(command.Id, cancellationToken);

        if (attachment == null)
        {
            throw new AttachmentNotFoundException(command.Id);
        }

        // Check authorization using HTTP context
        var canDelete = await authorizationService.CanCurrentUserDeleteAsync(command.Id, cancellationToken);
        if (!canDelete)
        {
            throw new UnauthorizedAccessException(
                $"User does not have permission to delete attachment {command.Id}");
        }

        // Delete physical files - must succeed before DB deletion
        var filesToDelete = new List<string> { attachment.StoragePath };
        if (!string.IsNullOrEmpty(attachment.ThumbnailPath))
        {
            filesToDelete.Add(attachment.ThumbnailPath);
        }

        try
        {
            await storageService.DeleteBatchAsync(filesToDelete, cancellationToken);
            
            logger.LogInformation(
                "Deleted {Count} physical file(s) for attachment {AttachmentId} by user {UserId}",
                filesToDelete.Count,
                command.Id,
                authorizationService.GetCurrentUserId());
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to delete physical files for attachment {AttachmentId}. Storage paths: {Paths}",
                command.Id,
                string.Join(", ", filesToDelete));
            
            throw new InvalidOperationException(
                $"Cannot delete attachment {command.Id} - physical file deletion failed. Please try again or contact support.",
                ex);
        }

        // Only hard delete DB record if storage deletion succeeded
        var success = await repository.DeleteAsync(command.Id, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Attachment deleted. ID: {AttachmentId} by user {UserId}", 
            command.Id,
            authorizationService.GetCurrentUserId());

        return new DeleteAttachmentResult(success);
    }
}
