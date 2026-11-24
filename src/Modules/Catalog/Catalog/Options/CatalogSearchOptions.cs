namespace Catalog.Options;

/// <summary>Product search through the Search module (<c>Catalog:Search</c>).</summary>
public sealed class CatalogSearchOptions
{
    /// <summary>Answer text searches and facets from the search index; <c>false</c> keeps every query in SQL.</summary>
    public bool UseSearchIndex { get; set; } = true;

    /// <summary>The deepest result (page times page size) read from the index; deeper pages are answered by SQL.</summary>
    public int MaxWindow { get; set; } = 1000;
}
