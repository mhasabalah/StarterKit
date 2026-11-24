using Shared.Core.MultiTenancy.Data;
using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Features.GetAdminProducts;

public class GetAdminProductsQueryValidator : AbstractValidator<GetAdminProductsQuery>
{
    public GetAdminProductsQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        this.AddProductFilterRules(localizer);
    }
}

/// <summary>Admin listing over the same filter pipeline as the public one; any status, optionally deleted rows.</summary>
public class GetAdminProductsHandler(
    CatalogDbContext dbContext,
    IProductSearch productSearch,
    ProductDtoMapper mapper)
    : IQueryHandler<GetAdminProductsQuery, PaginatedResult<ProductAdminDto>>
{
    public async Task<PaginatedResult<ProductAdminDto>> Handle(
        GetAdminProductsQuery query, CancellationToken cancellationToken)
    {
        var products = dbContext.Products.AsNoTracking();
        if (query.IncludeDeleted)
            products = products.IgnoreNonIsolationFilters();

        var page = await productSearch.SearchAsync(new ProductSearchRequest
        {
            Products = products,
            Filters = query,
            Paging = query,
            IncludesDeleted = query.IncludeDeleted // deleted products are never indexed: SQL answers
        }, cancellationToken);

        return page.Rows.Map(mapper.ToAdmin);
    }
}
