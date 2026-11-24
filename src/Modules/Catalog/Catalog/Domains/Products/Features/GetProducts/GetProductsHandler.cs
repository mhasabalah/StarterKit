using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Features.GetProducts;

public class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddProductFilterRules(localizer);
    }
}

/// <summary>
/// Public catalogue. Forces Status = Published (whatever the caller sent) — the endpoint does too, this is
/// defence in depth. <see cref="IProductSearch"/> reads through the DbContext (or the search index for a text search,
/// re-checked in SQL); nothing is tracked.
/// </summary>
public class GetProductsHandler(
    CatalogDbContext dbContext,
    IProductSearch productSearch,
    ProductDtoMapper mapper)
    : IQueryHandler<GetProductsQuery, PaginatedResult<ProductDto>>
{
    public async Task<PaginatedResult<ProductDto>> Handle(GetProductsQuery query, CancellationToken cancellationToken)
    {
        var publicQuery = query with { Status = ProductStatus.Published };
        var page = await productSearch.SearchAsync(new ProductSearchRequest
        {
            Products = dbContext.Products.AsNoTracking(),
            Filters = publicQuery,
            Paging = query
        }, cancellationToken);

        return page.Rows.Map(mapper.ToPublic);
    }
}
