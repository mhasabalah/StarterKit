using Shared.Contracts.CQRS;

namespace Search.Contracts.Querying;

/// <summary>
/// Searches one index of the current tenant, within the caller's branch scope, and returns ids, not entities: the
/// owning module loads the hits through its own DbContext and filters them again (ADR-0036).
/// </summary>
/// <remarks>
/// A trusted, internal port: send it from the owning module's handler, whose endpoint decides who may search. Always
/// pass the owner's visibility filters (for example <c>status = Published</c> for a public list), because the total
/// and the facet counts come from the index.
/// </remarks>
public sealed record SearchDocumentsQuery : IQuery<SearchDocumentsResult>
{
    /// <summary>The <see cref="Indexing.SearchIndexDefinition.Key"/> to search.</summary>
    public required string IndexKey { get; init; }

    /// <summary>
    /// The search text: words, <c>"quoted phrases"</c>, <c>-excluded</c> words and <c>OR</c>. Empty = browse (filters
    /// and sort only).
    /// </summary>
    public string? Text { get; init; }

    public SearchMode Mode { get; init; } = SearchMode.Full;

    /// <summary>All filters must match.</summary>
    public IReadOnlyList<SearchFilter> Filters { get; init; } = [];

    /// <summary>Facetable fields to count values of, over the filtered matches.</summary>
    public IReadOnlyList<string> Facets { get; init; } = [];

    /// <summary><c>null</c> = by relevance (with text) or the index's default sort (without).</summary>
    public SearchSort? Sort { get; init; }

    /// <summary>1-based.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    /// <summary>Return highlighted snippets of the highlight fields.</summary>
    public bool Highlight { get; init; }

    /// <summary>When nothing matches exactly, look for close spellings (reported as <see cref="SearchMatchMode.Fuzzy"/>).</summary>
    public bool Fuzzy { get; init; } = true;

    public const int TextMaxLength = 200;
}

public enum SearchMode
{
    /// <summary>Every word must match (prefix matching is not applied).</summary>
    Full = 0,

    /// <summary>As-you-type: the last word also matches as a prefix ("lap" finds "laptop").</summary>
    Autocomplete = 1
}

/// <summary>Order by a sortable filter field; ties are broken by document id.</summary>
public sealed record SearchSort(string Field, bool Descending = false);

/// <summary>
/// A condition on a filter field. All filters of a query must match. Search accepts only the filter types below and
/// checks each against the field's type.
/// </summary>
/// <param name="Field">A filter field of the index.</param>
public abstract record SearchFilter(string Field);

/// <summary>A keyword field equals <paramref name="Value"/>, or a keyword list contains it.</summary>
public sealed record KeywordFilter(string Field, string Value) : SearchFilter(Field);

/// <summary>
/// The field matches any of <paramref name="Values"/> (<see cref="SearchMatch.Any"/>), or, for a keyword list, contains
/// all of them (<see cref="SearchMatch.All"/>).
/// </summary>
public sealed record KeywordsFilter(string Field, IReadOnlyList<string> Values, SearchMatch Match = SearchMatch.Any)
    : SearchFilter(Field);

/// <summary>A number field within the bounds (both inclusive; <c>null</c> = open).</summary>
public sealed record NumberRangeFilter(string Field, decimal? Min, decimal? Max) : SearchFilter(Field);

/// <summary>A date field within the bounds (both inclusive, UTC; <c>null</c> = open).</summary>
public sealed record DateRangeFilter(string Field, DateTime? From, DateTime? To) : SearchFilter(Field);

public sealed record BoolFilter(string Field, bool Value) : SearchFilter(Field);

public enum SearchMatch
{
    Any = 0,
    All = 1
}
