namespace Catalog.Contracts.Products.Dtos;

/// <summary>
/// A page of products for a search, plus how they matched, facet counts and highlights. The paging shape is the one
/// of every product list, so clients that read only the page keep working.
/// </summary>
public sealed class SearchProductsResult : PaginatedResult<ProductDto>
{
    public ProductSearchMatch Match { get; set; }

    /// <summary>The requested facets over all matches, most frequent values first. Empty without the search index.</summary>
    public List<ProductFacetDto> Facets { get; set; } = [];

    /// <summary><c>false</c> when the counts were taken over a capped number of matches.</summary>
    public bool FacetsExhaustive { get; set; } = true;

    /// <summary>
    /// Per product id, per field (<c>nameEn</c>, <c>nameAr</c>, <c>description</c>): a snippet with the matches wrapped
    /// in <c>&lt;mark&gt;</c>; everything else in the snippet is HTML-encoded. Only with <c>highlight=true</c> and the
    /// search index.
    /// </summary>
    public Dictionary<Guid, Dictionary<string, string>> Highlights { get; set; } = [];
}

/// <param name="Values">Value (an id, an enum name, <c>true</c>/<c>false</c>) and how many matches have it.</param>
public sealed record ProductFacetDto(ProductFacet Facet, List<ProductFacetValueDto> Values);

public sealed record ProductFacetValueDto(string Value, int Count);
