using Catalog.Domains.Products.Mappings;
using Search.Contracts.Querying;
using static Catalog.Domains.Products.SearchIndex.ProductSearchIndex;

namespace Catalog.Domains.Products.SearchIndex;

/// <summary>
/// The one place where the catalogue's filters, sort columns and facets become index terms. A request the index cannot
/// express exactly (an unindexed sort column, too many ids) is declined, and the SQL search answers it instead: the
/// results never depend on which path answered.
/// </summary>
internal static class ProductSearchMapping
{
    /// <summary>Ids a single keyword filter may carry (the Search module's limit).</summary>
    public const int MaxFilterValues = 100;

    private static readonly Dictionary<string, string> SortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(ProductRow.Price)] = Fields.Price,
        [nameof(ProductRow.CreatedAt)] = Fields.CreatedAt,
        [nameof(ProductRow.PublishedAt)] = Fields.PublishedAt
    };

    private static readonly HashSet<string> SqlSortColumns = typeof(ProductRow).GetProperties()
        .Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<ProductFacet, string> FacetFields = new()
    {
        [ProductFacet.Category] = Fields.CategoryId,
        [ProductFacet.Tag] = Fields.TagIds,
        [ProductFacet.Type] = Fields.Type,
        [ProductFacet.Currency] = Fields.Currency,
        [ProductFacet.Featured] = Fields.IsFeatured
    };

    /// <summary>The highlight fields, by the names the API returns.</summary>
    private static readonly Dictionary<string, string> HighlightNames = new(StringComparer.Ordinal)
    {
        [Fields.NameEn] = "nameEn",
        [Fields.NameAr] = "nameAr",
        [Fields.Description] = "description"
    };

    /// <summary>
    /// No column: relevance (or the index's default order without text). A product column the index does not sort
    /// on: declined. An unknown column is ignored, as the SQL search does.
    /// </summary>
    public static bool TryMapSort(BaseSortQuery sort, out SearchSort? mapped)
    {
        mapped = null;
        if (string.IsNullOrWhiteSpace(sort.SortColumn))
            return true;
        if (SortFields.TryGetValue(sort.SortColumn, out var field))
        {
            mapped = new SearchSort(field, Descending: sort.SortBy != SortBy.Ascending);
            return true;
        }

        return !SqlSortColumns.Contains(sort.SortColumn);
    }

    /// <summary>Every filter of <paramref name="query"/> except the text; false when one cannot be expressed.</summary>
    public static bool TryMapFilters(
        IProductFilterQuery query, IReadOnlyCollection<Guid>? categoryIds, out List<SearchFilter> filters)
    {
        filters = [];
        if (query.Status is { } status)
            filters.Add(new KeywordFilter(Fields.Status, status.ToString()));
        if (query.Type is { } type)
            filters.Add(new KeywordFilter(Fields.Type, type.ToString()));
        if (query.Currency is { } currency)
            filters.Add(new KeywordFilter(Fields.Currency, currency.ToString()));
        if (query.OwnerId is { } owner)
            filters.Add(new KeywordFilter(Fields.OwnerId, owner.ToString()));
        if (query.IsFeatured is { } featured)
            filters.Add(new BoolFilter(Fields.IsFeatured, featured));
        if (query.MinPrice is not null || query.MaxPrice is not null)
            filters.Add(new NumberRangeFilter(Fields.Price, query.MinPrice, query.MaxPrice));

        return TryAddIds(filters, Fields.CategoryId, categoryIds, SearchMatch.Any)
               && TryAddIds(filters, Fields.TagIds, query.TagIds, SearchMatch.All)
               && TryAddIds(filters, Fields.AttributeTypeIds, query.AttributeTypeIds, SearchMatch.All);
    }

    public static List<string> FacetFieldsOf(IEnumerable<ProductFacet> facets) =>
        facets.Distinct().Where(FacetFields.ContainsKey).Select(f => FacetFields[f]).ToList();

    public static List<ProductFacetDto> ToFacets(IEnumerable<SearchFacet> facets) =>
        facets
            .Select(f => (Facet: FacetFields.FirstOrDefault(m => m.Value == f.Field).Key, f.Values))
            .Where(f => f.Facet != default)
            .Select(f => new ProductFacetDto(f.Facet, f.Values.Select(v => new ProductFacetValueDto(v.Value, v.Count)).ToList()))
            .ToList();

    public static Dictionary<string, string> ToHighlights(IReadOnlyDictionary<string, string> highlights) =>
        highlights
            .Where(h => HighlightNames.ContainsKey(h.Key))
            .ToDictionary(h => HighlightNames[h.Key], h => h.Value, StringComparer.Ordinal);

    private static bool TryAddIds(List<SearchFilter> filters, string field, IEnumerable<Guid>? ids, SearchMatch match)
    {
        if (ids is null)
            return true;

        var values = ids.Distinct().Select(id => id.ToString()).ToList();
        if (values.Count == 0)
            return true;
        if (values.Count > MaxFilterValues)
            return false;

        filters.Add(new KeywordsFilter(field, values, match));
        return true;
    }
}

/// <summary>The filters of a query without its text: the hydration re-applies visibility and filters, not the match.</summary>
internal sealed record ProductFiltersWithoutText(IProductFilterQuery Inner) : IProductFilterQuery
{
    public string? Search => null;
    public ProductType? Type => Inner.Type;
    public ProductStatus? Status => Inner.Status;
    public Guid? CategoryId => Inner.CategoryId;
    public bool? IncludeSubcategories => Inner.IncludeSubcategories;
    public decimal? MinPrice => Inner.MinPrice;
    public decimal? MaxPrice => Inner.MaxPrice;
    public Currency? Currency => Inner.Currency;
    public Guid[]? TagIds => Inner.TagIds;
    public Guid[]? AttributeTypeIds => Inner.AttributeTypeIds;
    public Guid? OwnerId => Inner.OwnerId;
    public bool? IsFeatured => Inner.IsFeatured;
}
