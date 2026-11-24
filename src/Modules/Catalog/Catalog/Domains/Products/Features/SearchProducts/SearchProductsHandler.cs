using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Features.SearchProducts;

public class SearchProductsQueryValidator : AbstractValidator<SearchProductsQuery>
{
    public SearchProductsQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddProductFilterRules(localizer);

        RuleFor(x => x.SearchTerm)
            .NotEmpty().WithMessage(_ => localizer["Validation.SearchTerm.Required"])
            .MaximumLength(CatalogValidationRules.SearchTermMaxLength)
            .WithMessage(_ => localizer["Validation.SearchTerm.MaxLength"]);

        RuleForEach(x => x.Facets).IsInEnum().WithMessage(_ => localizer["Validation.Facet.Invalid"]);
    }
}

/// <summary>
/// Published products matching the term, with the catalogue filters. With the search index: relevance order, facets,
/// highlights and close spellings; otherwise name (en/ar), SKU or description contains the (escaped) term.
/// </summary>
public class SearchProductsHandler(
    CatalogDbContext dbContext,
    IProductSearch productSearch,
    ProductDtoMapper mapper)
    : IQueryHandler<SearchProductsQuery, SearchProductsResult>
{
    public async Task<SearchProductsResult> Handle(SearchProductsQuery query, CancellationToken cancellationToken)
    {
        var page = await productSearch.SearchAsync(new ProductSearchRequest
        {
            Products = dbContext.Products.AsNoTracking(),
            Filters = query, // Status is always Published (IProductFilterQuery.Status)
            Paging = query,
            Facets = query.Facets ?? [],
            Highlight = query.Highlight == true
        }, cancellationToken);

        var mapped = page.Rows.Map(mapper.ToPublic);
        return new SearchProductsResult
        {
            PageInfoResult = mapped.PageInfoResult,
            OrderBy = mapped.OrderBy,
            OrderByDesc = mapped.OrderByDesc,
            Result = mapped.Result,
            Match = page.Match,
            Facets = page.Facets,
            FacetsExhaustive = page.FacetsExhaustive,
            Highlights = page.Highlights
        };
    }
}
