using Attachment.Contracts.Attachments.Commands;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.BackgroundJobs;
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

/// <summary>
/// Hides the attachment at once (<c>PendingDeletion</c> + soft delete, saved here) and enqueues
/// <see cref="DeleteAttachmentFilesJob"/> to delete its files and row. The request never waits for (or fails on)
/// storage; the job retries and the reconciliation sweeper backs it up.
/// </summary>
public class DeleteAttachmentHandler(
    IAttachmentRepository repository,
    IAttachmentAccessService accessService,
    IAttachmentBackgroundJobs backgroundJobs,
    TimeProvider time,
    ILogger<DeleteAttachmentHandler> logger)
    : ICommandHandler<DeleteAttachmentCommand, DeleteAttachmentResult>
{
    public async Task<DeleteAttachmentResult> Handle(
        DeleteAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var attachment = await repository.GetByIdAsync(command.Id, cancellationToken)
                         ?? throw new AttachmentNotFoundException(command.Id);

        if (!await accessService.CanDeleteAsync(attachment, command.EnforceEntityAccessPolicy, cancellationToken))
        {
            if (!accessService.Current.IsAuthenticated)
                throw new UnauthorizedException("Authentication is required to delete attachments.");

            throw new ForbiddenException("You are not allowed to delete this attachment.");
        }

        attachment.RequestDeletion(time.GetUtcNow().UtcDateTime);
        await repository.SaveChangesAsync(cancellationToken);

        backgroundJobs.EnqueueFileDeletion([attachment.Id]);

        logger.LogInformation("Attachment {AttachmentId} deleted by {UserId}", command.Id, accessService.Current.UserId);
        return new DeleteAttachmentResult(true);
    }
}
