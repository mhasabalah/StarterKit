using Attachment.Contracts.Attachments.Queries;
using Attachment.Infrastructure.Authorization;
using Attachment.Resources;
using Microsoft.Extensions.Localization;

namespace Attachment.Domains.Attachments.Features.GetAttachmentsByEntity;

public class GetAttachmentsByEntityQueryValidator
    : AbstractValidator<GetAttachmentsByEntityQuery>
{
    public GetAttachmentsByEntityQueryValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.EntityId)
            .NotEmpty().WithMessage(_ => localizer["Validation.EntityId.Required"]);
    }
}

public class GetAttachmentsByEntityHandler(
    IAttachmentRepository repository,
    IAttachmentAuthorizationService authorizationService,
    ILogger<GetAttachmentsByEntityHandler> logger)
    : IQueryHandler<GetAttachmentsByEntityQuery, GetAttachmentsByEntityResult>
{
    public async Task<GetAttachmentsByEntityResult> Handle(
        GetAttachmentsByEntityQuery query,
        CancellationToken cancellationToken)
    {
        var userId = authorizationService.GetCurrentUserId();
        var isAdmin = authorizationService.IsCurrentUserAdmin();

        var attachments = await repository.GetByEntityAsync(
            query.EntityType,
            query.EntityId,
            cancellationToken);

        // Filter attachments based on access permissions
        var accessibleAttachments = attachments
            .Where(a => a.CanBeAccessedBy(userId ?? Guid.Empty, isAdmin))
            .ToList();

        if (accessibleAttachments.Count < attachments.Count)
        {
            logger.LogInformation(
                "User {UserId} filtered to {AccessibleCount}/{TotalCount} attachments for entity {EntityType}#{EntityId}",
                userId,
                accessibleAttachments.Count,
                attachments.Count,
                query.EntityType,
                query.EntityId);
        }

        var dtos = accessibleAttachments.Adapt<List<AttachmentDto>>();
        return new GetAttachmentsByEntityResult(dtos);
    }
}
