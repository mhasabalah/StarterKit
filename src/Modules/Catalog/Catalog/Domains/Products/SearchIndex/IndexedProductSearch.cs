using Catalog.Domains.Products.Mappings;
using Catalog.Options;
using Microsoft.Extensions.Options;
using Search.Contracts.Querying;
using Shared.Core.Pagination;

namespace Catalog.Domains.Products.SearchIndex;

/// <summary>
/// Answers product searches from the search index (relevance, stemming, Arabic normalization, close spellings, facets,
/// highlights), then loads the hits through <see cref="CatalogDbContext"/> with every filter and visibility rule
/// re-applied, in the index's order. The index never decides what a caller may see.
/// <para>
/// <see cref="SqlProductSearch"/> answers instead when the index is off (<c>Catalog:Search:UseSearchIndex</c>), there is
/// neither text nor a facet (plain browsing stays in SQL), deleted products are included, the request cannot be
/// expressed in the index (<see cref="ProductSearchMapping"/>), the page is beyond <c>Catalog:Search:MaxWindow</c>, or
/// the index does not answer (not built yet, engine down).
/// </para>
/// The total comes from the index; a hit the caller may no longer see (a change not indexed yet) is left out of the
/// page.
/// </summary>
public sealed class IndexedProductSearch(
    SqlProductSearch sql,
    CatalogDbContext dbContext,
    IHttpContextService httpContextService,
    ISender sender,
    IOptions<CatalogOptions> options,
    ILogger<IndexedProductSearch> logger) : IProductSearch
{
    public async Task<ProductSearchPage> SearchAsync(ProductSearchRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value.Search;
        var text = request.Filters.Search?.Trim();
        if (!settings.UseSearchIndex || request.IncludesDeleted || (string.IsNullOrEmpty(text) && request.Facets.Count == 0))
            return await sql.SearchAsync(request, cancellationToken);

        var (page, size) = PagingOptions.Normalize(request.Paging.PageNumber, request.Paging.PageSize);
        var categoryIds = await ProductQueryBuilder.ResolveCategoryIdsAsync(dbContext, request.Filters, cancellationToken);
        if ((long)page * size > settings.MaxWindow
            || !ProductSearchMapping.TryMapSort(request.Paging, out var sort)
            || !ProductSearchMapping.TryMapFilters(request.Filters, categoryIds, out var filters))
        {
            return await sql.SearchAsync(request, cancellationToken);
        }

        var result = await sender.Send(new SearchDocumentsQuery
        {
            IndexKey = ProductSearchIndex.Key,
            Text = text,
            Mode = SearchMode.Full,
            Filters = filters,
            Facets = ProductSearchMapping.FacetFieldsOf(request.Facets),
            Sort = sort,
            Page = page,
            PageSize = size,
            Highlight = request.Highlight
        }, cancellationToken);

        if (result.Status != SearchStatus.Ok)
        {
            logger.LogDebug("Product search answered by SQL: the search index answered {Status}", result.Status);
            return await sql.SearchAsync(request, cancellationToken);
        }

        var rows = await HydrateAsync(request, categoryIds, result.Hits, cancellationToken);
        return new ProductSearchPage(
            new PaginatedResult<ProductRow>
            {
                PageInfoResult = new PageInfoResult { TotalItems = result.Total, PageSize = size, CurrentPage = page },
                Result = rows
            },
            result.MatchMode switch
            {
                SearchMatchMode.Fuzzy => ProductSearchMatch.Fuzzy,
                SearchMatchMode.Browse => ProductSearchMatch.Browse,
                _ => ProductSearchMatch.Exact
            })
        {
            Facets = ProductSearchMapping.ToFacets(result.Facets),
            FacetsExhaustive = result.FacetsExhaustive,
            Highlights = result.Hits
                .Where(hit => hit.Highlights.Count > 0 && Guid.TryParse(hit.Id, out _))
                .ToDictionary(hit => Guid.Parse(hit.Id), hit => ProductSearchMapping.ToHighlights(hit.Highlights))
        };
    }

    /// <summary>The hits as product rows, through the same filters (without the text) and in the index's order.</summary>
    private async Task<List<ProductRow>> HydrateAsync(
        ProductSearchRequest request, IReadOnlyCollection<Guid>? categoryIds, IReadOnlyList<SearchHit> hits,
        CancellationToken cancellationToken)
    {
        var ids = hits.Select(hit => Guid.TryParse(hit.Id, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        if (ids.Count == 0)
            return [];

        var rows = await ProductQueryBuilder
            .ApplyFilters(request.Products, new ProductFiltersWithoutText(request.Filters), categoryIds)
            .Where(p => ids.Contains(p.Id))
            .ToProductRows(dbContext.WishlistItems, httpContextService.GetUserIdAsGuid() ?? Guid.Empty, includeGallery: false)
            .ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(r => r.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}
