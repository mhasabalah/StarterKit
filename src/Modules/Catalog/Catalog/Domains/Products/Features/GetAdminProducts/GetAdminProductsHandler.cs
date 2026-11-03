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
    IHttpContextService httpContextService,
    ProductDtoMapper mapper)
    : IQueryHandler<GetAdminProductsQuery, PaginatedResult<ProductAdminDto>>
{
    public async Task<PaginatedResult<ProductAdminDto>> Handle(
        GetAdminProductsQuery query, CancellationToken cancellationToken)
    {
        var categoryIds = await ProductQueryBuilder.ResolveCategoryIdsAsync(dbContext, query, cancellationToken);

        var products = dbContext.Products.AsNoTracking();
        if (query.IncludeDeleted)
            products = products.IgnoreNonIsolationFilters();

        var rows = ProductQueryBuilder
            .ApplyFilters(products, query, categoryIds)
            .ToProductRows(dbContext.WishlistItems, httpContextService.GetUserIdAsGuid() ?? Guid.Empty,
                includeGallery: false)
            .Sorted(query);

        var page = await rows.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
        return page.Map(mapper.ToAdmin);
    }
}
