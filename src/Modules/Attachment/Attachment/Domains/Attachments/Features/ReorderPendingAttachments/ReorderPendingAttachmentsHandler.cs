using Attachment.Contracts.Attachments.Commands;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Security;
using Attachment.Resources;
using Microsoft.Extensions.Localization;

namespace Attachment.Domains.Attachments.Features.ReorderPendingAttachments;

public class ReorderPendingAttachmentsCommandValidator : AbstractValidator<ReorderPendingAttachmentsCommand>
{
    public ReorderPendingAttachmentsCommandValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage(_ => localizer["Validation.SessionId.Required"]);

        RuleFor(x => x.OrderedIds)
            .NotEmpty().WithMessage(_ => localizer["Validation.OrderedIds.Required"])
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage(_ => localizer["Validation.OrderedIds.Unique"]);
    }
}

public class ReorderPendingAttachmentsHandler(
    IPendingAttachmentRepository repository,
    IPendingSessionService sessionService,
    ILogger<ReorderPendingAttachmentsHandler> logger)
    : ICommandHandler<ReorderPendingAttachmentsCommand, bool>
{
    public async Task<bool> Handle(ReorderPendingAttachmentsCommand command, CancellationToken cancellationToken)
    {
        if (!sessionService.IsOwnedByCurrentCaller(command.SessionId))
            throw new PendingSessionAccessDeniedException();

        var pending = await repository.GetBySessionIdAsync(command.SessionId, cancellationToken);
        var byId = pending.ToDictionary(p => p.Id);

        var invalidIds = command.OrderedIds.Where(id => !byId.ContainsKey(id)).ToList();
        if (invalidIds.Count > 0)
            throw new BadRequestException(
                $"The following IDs do not belong to the session: {string.Join(", ", invalidIds)}");

        for (var i = 0; i < command.OrderedIds.Count; i++)
            byId[command.OrderedIds[i]].UpdateDisplayOrder(i);

        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Reordered {Count} pending attachments of session {SessionId}",
            command.OrderedIds.Count, command.SessionId);
        return true;
    }
}
