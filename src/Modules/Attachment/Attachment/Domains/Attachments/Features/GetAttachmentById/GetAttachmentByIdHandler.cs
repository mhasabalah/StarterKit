using Attachment.Contracts.Attachments.Queries;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Authorization;
using Attachment.Resources;
using Microsoft.Extensions.Localization;

namespace Attachment.Domains.Attachments.Features.GetAttachmentById;

public class GetAttachmentByIdQueryValidator : AbstractValidator<GetAttachmentByIdQuery>
{
    public GetAttachmentByIdQueryValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage(_ => localizer["Validation.AttachmentId.Required"]);
    }
}

public class GetAttachmentByIdHandler(
    IAttachmentRepository repository,
    IAttachmentAuthorizationService authorizationService,
    ILogger<GetAttachmentByIdHandler> logger)
    : IQueryHandler<GetAttachmentByIdQuery, GetAttachmentByIdResult>
{
    public async Task<GetAttachmentByIdResult> Handle(
        GetAttachmentByIdQuery query,
        CancellationToken cancellationToken)
    {
        var attachment = await repository.GetByIdAsync(query.Id, cancellationToken);

        if (attachment == null)
        {
            throw new AttachmentNotFoundException(query.Id);
        }

        // Check if user can access this attachment
        var canAccess = await authorizationService.CanCurrentUserAccessAsync(query.Id, cancellationToken);
        if (!canAccess)
        {
            logger.LogWarning(
                "User {UserId} attempted to access attachment {AttachmentId} without permission",
                authorizationService.GetCurrentUserId(),
                query.Id);
            
            throw new UnauthorizedAccessException(
                $"You do not have permission to access attachment {query.Id}");
        }

        var dto = attachment.Adapt<AttachmentDto>();
        return new GetAttachmentByIdResult(dto);
    }
}
