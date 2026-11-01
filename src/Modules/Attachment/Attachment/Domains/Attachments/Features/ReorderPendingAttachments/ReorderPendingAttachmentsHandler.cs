using Attachment.Contracts.Attachments.Commands;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Exceptions;
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
    ILogger<ReorderPendingAttachmentsHandler> logger)
    : ICommandHandler<ReorderPendingAttachmentsCommand, bool>
{
    public async Task<bool> Handle(
        ReorderPendingAttachmentsCommand command,
        CancellationToken cancellationToken)
    {
        var pendingAttachments = await repository.GetBySessionIdAsync(
            command.SessionId,
            cancellationToken);

        if (!pendingAttachments.Any())
        {
            throw new PendingAttachmentNotFoundException(Guid.Empty);
        }

        // Verify all IDs belong to this session
        var sessionIds = pendingAttachments.Select(p => p.Id).ToHashSet();
        var invalidIds = command.OrderedIds.Where(id => !sessionIds.Contains(id)).ToList();

        if (invalidIds.Any())
        {
            throw new InvalidOperationException(
                $"The following IDs do not belong to session {command.SessionId}: {string.Join(", ", invalidIds)}");
        }

        // Update display order
        for (int i = 0; i < command.OrderedIds.Count; i++)
        {
            var attachment = pendingAttachments.First(p => p.Id == command.OrderedIds[i]);
            attachment.UpdateDisplayOrder(i);
        }

        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Reordered {Count} pending attachments for session {SessionId}",
            command.OrderedIds.Count,
            command.SessionId);

        return true;
    }
}
