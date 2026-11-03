using Attachment.Contracts.Attachments.Commands;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Security;
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
    IPendingSessionService sessionService,
    ILogger<DeletePendingAttachmentHandler> logger)
    : ICommandHandler<DeletePendingAttachmentCommand, bool>
{
    public async Task<bool> Handle(DeletePendingAttachmentCommand command, CancellationToken cancellationToken)
    {
        if (!sessionService.IsOwnedByCurrentCaller(command.SessionId))
            throw new PendingSessionAccessDeniedException();

        var pending = await repository.GetByIdAsync(command.Id, cancellationToken);
        if (pending is null || !pending.BelongsToSession(command.SessionId))
            throw new PendingAttachmentNotFoundException(command.Id);

        var paths = new List<string> { pending.StoragePath };
        if (!string.IsNullOrEmpty(pending.ThumbnailPath))
            paths.Add(pending.ThumbnailPath);

        var result = await storageService.DeleteBatchAsync(paths, cancellationToken);
        if (!result.AllSucceeded)
            throw new FileStorageException("The file could not be deleted. Please try again.");

        pending.MarkAsDeleted();
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Pending attachment {Id} of session {SessionId} deleted", command.Id, command.SessionId);
        return true;
    }
}
