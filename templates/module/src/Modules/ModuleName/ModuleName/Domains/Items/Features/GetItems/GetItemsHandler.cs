using System.Globalization;

namespace ModuleName.Domains.Items.Features.GetItems;

public class GetItemsQueryValidator : AbstractValidator<GetItemsQuery>
{
    public GetItemsQueryValidator(IStringLocalizer<ModuleNameResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ItemRules.MaxPageSize)
            .WithMessage(_ => localizer["Validation.PageSize.Range"].Value
                .Replace("{MaxPageSize}", ItemRules.MaxPageSize.ToString(CultureInfo.InvariantCulture)))
            .When(x => x.PageSize.HasValue);
        RuleFor(x => x.Search).MaximumLength(ItemRules.SearchMaxLength)
            .WithMessage(_ => localizer["Validation.Search.MaxLength"].Value
                .Replace("{MaxLength}", ItemRules.SearchMaxLength.ToString(CultureInfo.InvariantCulture)));
    }
}

/// <summary>Query side: no tracking, filter + projection + paging in SQL, newest first (id breaks ties).</summary>
public class GetItemsHandler(ModuleNameDbContext dbContext) : IQueryHandler<GetItemsQuery, PaginatedResult<ItemDto>>
{
    public Task<PaginatedResult<ItemDto>> Handle(GetItemsQuery query, CancellationToken cancellationToken)
    {
        var items = dbContext.Items.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var code = term.ToUpperInvariant();
            items = items.Where(x => x.Code.StartsWith(code) || EF.Functions.ILike(x.Name, "%" + term + "%"));
        }

        return items
            .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(ItemRules.Projection)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
