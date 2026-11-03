using Attachment.Contracts.Attachments.Queries;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.Storage;
using Attachment.Resources;
using Microsoft.Extensions.Localization;
using Shared.Core.Pagination;

namespace Attachment.Domains.Attachments.Features.GetAttachmentsByEntity;

public class GetAttachmentsByEntityQueryValidator : AbstractValidator<GetAttachmentsByEntityQuery>
{
    public GetAttachmentsByEntityQueryValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.EntityId)
            .NotEmpty().WithMessage(_ => localizer["Validation.EntityId.Required"]);

        RuleFor(x => x.PageNumber).ValidPageNumber();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

/// <summary>
/// Ready attachments of an entity, one page, newest first. Callers allowed by the entity policy (or admins) see
/// all of them; everyone else sees only public ones plus their own uploads. Filtering, counting and paging
/// happen in SQL. Without paging parameters the page size is <c>Paging:MaxPageSize</c>.
/// </summary>
public class GetAttachmentsByEntityHandler(
    IAttachmentRepository repository,
    IAttachmentAccessService accessService,
    IAttachmentUrls urls)
    : IQueryHandler<GetAttachmentsByEntityQuery, GetAttachmentsByEntityResult>
{
    public async Task<GetAttachmentsByEntityResult> Handle(
        GetAttachmentsByEntityQuery query,
        CancellationToken cancellationToken)
    {
        var (page, size) = PagingOptions.Normalize(query.PageNumber, query.PageSize ?? PagingOptions.MaxPageSize);

        var canReadAll = await accessService.CanReadEntityAsync(query.EntityType, query.EntityId, cancellationToken);

        var (items, total) = await repository.GetByEntityVisiblePageAsync(
            query.EntityType, query.EntityId, canReadAll, accessService.Current.UserId, page, size, cancellationToken);

        return new GetAttachmentsByEntityResult(items.Select(urls.ToDto).ToList())
        {
            TotalCount = total,
            PageNumber = page,
            PageSize = size
        };
    }
}
