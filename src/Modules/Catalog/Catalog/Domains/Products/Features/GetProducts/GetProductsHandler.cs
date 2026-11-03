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
/// defence in depth. Query handlers read through the DbContext and project in SQL; nothing is tracked.
/// </summary>
public class GetProductsHandler(
    CatalogDbContext dbContext,
    IHttpContextService httpContextService,
    ProductDtoMapper mapper)
    : IQueryHandler<GetProductsQuery, PaginatedResult<ProductDto>>
{
    public async Task<PaginatedResult<ProductDto>> Handle(GetProductsQuery query, CancellationToken cancellationToken)
    {
        var publicQuery = query with { Status = ProductStatus.Published };
        var categoryIds = await ProductQueryBuilder.ResolveCategoryIdsAsync(dbContext, publicQuery, cancellationToken);

        var rows = ProductQueryBuilder
            .ApplyFilters(dbContext.Products.AsNoTracking(), publicQuery, categoryIds)
            .ToProductRows(dbContext.WishlistItems, httpContextService.GetUserIdAsGuid() ?? Guid.Empty,
                includeGallery: false)
            .Sorted(query);

        var page = await rows.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
        return page.Map(mapper.ToPublic);
    }
}
