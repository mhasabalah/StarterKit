using Search.Contracts.Querying;
using Shared.Contracts.Localization;

namespace Search.Contracts.Indexing;

/// <summary>
/// What an owning module makes searchable: one index per entity, registered with
/// <see cref="SearchIndexServiceCollectionExtensions.AddSearchIndex{TSource}"/> and validated when the host starts.
/// The definition is engine-neutral; every engine maps it to its own index (ADR-0036).
/// </summary>
/// <remarks>
/// Changing anything but <see cref="Name"/> changes the index fingerprint, which rebuilds the index in every tenant.
/// </remarks>
public sealed record SearchIndexDefinition
{
    /// <summary>
    /// <c>&lt;module&gt;.&lt;entity&gt;</c>, lower case (letters, digits, <c>_</c>, <c>-</c>), at most
    /// <see cref="KeyMaxLength"/> characters, for example <c>catalog.products</c>. Stored with every document: never
    /// rename it.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Display name for the admin views.</summary>
    public required LocalizedText Name { get; init; }

    /// <summary>The fields matched by the search text (at least one).</summary>
    public required IReadOnlyList<SearchTextField> TextFields { get; init; }

    /// <summary>The typed values a query can filter, facet and sort on.</summary>
    public IReadOnlyList<SearchFilterField> FilterFields { get; init; } = [];

    /// <summary>The order of a query without text (browsing); <c>null</c> orders by document id.</summary>
    public SearchSort? DefaultSort { get; init; }

    public const int KeyMaxLength = 100;
}

/// <summary>A field matched by the search text.</summary>
/// <param name="Name">Lower snake case (<c>name_en</c>), unique within the index, at most 40 characters.</param>
/// <param name="Weight">How much a match in this field counts for relevance.</param>
/// <param name="Analyzer">How the text is split and reduced to words.</param>
public sealed record SearchTextField(string Name, SearchWeight Weight, SearchAnalyzer Analyzer)
{
    /// <summary>The field feeds autocomplete and the typo-tolerant fallback (names, codes; not long descriptions).</summary>
    public bool Suggest { get; init; }

    /// <summary>Search returns a highlighted snippet of this field.</summary>
    public bool Highlight { get; init; }

    /// <summary>Longer text is cut before indexing.</summary>
    public int MaxLength { get; init; } = DefaultMaxLength;

    public const int DefaultMaxLength = 4000;
}

/// <summary>A typed value a query can filter on, and optionally facet or sort on.</summary>
/// <param name="Name">Lower snake case (<c>category_id</c>), unique within the index, at most 40 characters.</param>
/// <param name="Type">The value type; documents and queries must use the matching type.</param>
public sealed record SearchFilterField(string Name, SearchFieldType Type)
{
    /// <summary>Queries can ask for value counts (<see cref="SearchFieldType.Keyword"/>, <see cref="SearchFieldType.KeywordList"/>, <see cref="SearchFieldType.Boolean"/> only).</summary>
    public bool Facetable { get; init; }

    /// <summary>Queries can order by this field (not <see cref="SearchFieldType.KeywordList"/>).</summary>
    public bool Sortable { get; init; }
}

/// <summary>Relevance weight of a text field, lowest to highest.</summary>
public enum SearchWeight
{
    Low = 0,
    Medium = 1,
    High = 2,
    Highest = 3
}

/// <summary>How a text field is split into words and reduced for matching. Every analyzer also normalizes Arabic and Latin text.</summary>
public enum SearchAnalyzer
{
    /// <summary>Words as written: codes, SKUs, names in any language.</summary>
    Simple = 0,

    /// <summary>English stemming ("running" matches "run").</summary>
    English = 1,

    /// <summary>Arabic stemming.</summary>
    Arabic = 2,

    /// <summary>English and Arabic stemming, for free text that may be in either language.</summary>
    Bilingual = 3
}

/// <summary>The type of a filter field.</summary>
public enum SearchFieldType
{
    /// <summary>One exact value: an id, a status, a code.</summary>
    Keyword = 0,

    /// <summary>Several exact values: tag ids.</summary>
    KeywordList = 1,

    Number = 2,

    /// <summary>A UTC date and time.</summary>
    Date = 3,

    Boolean = 4
}
