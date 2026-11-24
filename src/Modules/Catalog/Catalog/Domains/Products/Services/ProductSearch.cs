using Catalog.Domains.Products.Mappings;

namespace Catalog.Domains.Products.Services;

/// <summary>What a product list or search asks for: the visible products, the filters, the page and the extras.</summary>
public sealed record ProductSearchRequest
{
    /// <summary>The products the caller may see before filtering (the admin listing may include deleted ones).</summary>
    public required IQueryable<Product> Products { get; init; }

    public required IProductFilterQuery Filters { get; init; }

    public required BaseQuery Paging { get; init; }

    /// <summary><see cref="Products"/> includes soft-deleted products (never in the search index).</summary>
    public bool IncludesDeleted { get; init; }

    public IReadOnlyCollection<ProductFacet> Facets { get; init; } = [];

    public bool Highlight { get; init; }
}

/// <summary>A page of product rows and what the search could add to it.</summary>
public sealed record ProductSearchPage(PaginatedResult<ProductRow> Rows, ProductSearchMatch Match)
{
    public List<ProductFacetDto> Facets { get; init; } = [];

    public bool FacetsExhaustive { get; init; } = true;

    public Dictionary<Guid, Dictionary<string, string>> Highlights { get; init; } = [];
}

/// <summary>
/// Finds a page of products for the list and search queries. <see cref="SqlProductSearch"/> filters in SQL ("contains"
/// matching); with the Search module, an indexed implementation answers searches from the search index and falls back
/// to SQL whenever the index cannot answer. Visibility (tenant, branch, status, deletion) is always re-applied in SQL.
/// </summary>
public interface IProductSearch
{
    Task<ProductSearchPage> SearchAsync(ProductSearchRequest request, CancellationToken cancellationToken);
}

/// <summary>The filter pipeline of <see cref="ProductQueryBuilder"/> in one SQL statement, newest first by default.</summary>
public sealed class SqlProductSearch(CatalogDbContext dbContext, IHttpContextService httpContextService) : IProductSearch
{
    public async Task<ProductSearchPage> SearchAsync(ProductSearchRequest request, CancellationToken cancellationToken)
    {
        var categoryIds = await ProductQueryBuilder.ResolveCategoryIdsAsync(dbContext, request.Filters, cancellationToken);

        var rows = ProductQueryBuilder
            .ApplyFilters(request.Products, request.Filters, categoryIds)
            .ToProductRows(dbContext.WishlistItems, httpContextService.GetUserIdAsGuid() ?? Guid.Empty, includeGallery: false)
            .Sorted(request.Paging);

        var page = await rows.PagedResult(request.Paging.PageNumber, request.Paging.PageSize, cancellationToken);
        var match = string.IsNullOrWhiteSpace(request.Filters.Search) ? ProductSearchMatch.Browse : ProductSearchMatch.Contains;
        return new ProductSearchPage(page, match);
    }
}
