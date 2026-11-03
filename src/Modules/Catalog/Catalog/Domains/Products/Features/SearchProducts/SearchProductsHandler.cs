using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Features.SearchProducts;

public class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddPagingRules(localizer);

        RuleFor(x => x.SearchTerm)
            .NotEmpty().WithMessage(_ => localizer["Validation.SearchTerm.Required"])
            .MaximumLength(CatalogValidationRules.SearchTermMaxLength)
            .WithMessage(_ => localizer["Validation.SearchTerm.MaxLength"]);
    }
}

/// <summary>Published products whose name (en/ar), SKU or description contains the (escaped) term.</summary>
public class SearchProductsHandler(
    CatalogDbContext dbContext,
    IHttpContextService httpContextService,
    ProductDtoMapper mapper)
    : IQueryHandler<SearchProductsQuery, PaginatedResult<ProductDto>>
{
    public async Task<PaginatedResult<ProductDto>> Handle(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        var products = dbContext.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Published);

        var rows = ProductQueryBuilder.ApplySearch(products, query.SearchTerm)
            .ToProductRows(dbContext.WishlistItems, httpContextService.GetUserIdAsGuid() ?? Guid.Empty,
                includeGallery: false)
            .Sorted(query);

        var page = await rows.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
        return page.Map(mapper.ToPublic);
    }
}
