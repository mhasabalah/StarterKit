namespace Search.Contracts.Querying;

/// <summary>
/// The answer of <see cref="SearchDocumentsQuery"/>. Check <see cref="Status"/> first: anything but
/// <see cref="SearchStatus.Ok"/> means the index could not answer, and the owner falls back to its own SQL search.
/// </summary>
public sealed record SearchDocumentsResult
{
    public required SearchStatus Status { get; init; }

    /// <summary>The page of hits, best first (or in the requested order).</summary>
    public IReadOnlyList<SearchHit> Hits { get; init; } = [];

    /// <summary>All matches, not only this page (capped by the engine's result window).</summary>
    public int Total { get; init; }

    public IReadOnlyList<SearchFacet> Facets { get; init; } = [];

    /// <summary><c>false</c> when the facet counts were taken over a capped number of matches.</summary>
    public bool FacetsExhaustive { get; init; } = true;

    public SearchMatchMode MatchMode { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    /// <summary>A result for an index that cannot answer (<paramref name="status"/> is not <see cref="SearchStatus.Ok"/>).</summary>
    public static SearchDocumentsResult Unavailable(SearchStatus status, int page, int pageSize) =>
        status == SearchStatus.Ok
            ? throw new ArgumentOutOfRangeException(nameof(status), status, "Use a real result for Ok.")
            : new SearchDocumentsResult { Status = status, Page = page, PageSize = pageSize };
}

/// <summary>One matching document.</summary>
/// <param name="Id">The <see cref="Indexing.SearchDocument.Id"/>.</param>
/// <param name="Score">Relevance; comparable only within one result.</param>
/// <param name="Highlights">
/// Per highlight field, a snippet of the original text with the matches wrapped in <c>&lt;mark&gt;</c>; the rest of the
/// text is HTML-encoded, so the snippet can be rendered as HTML.
/// </param>
public sealed record SearchHit(string Id, double Score, IReadOnlyDictionary<string, string> Highlights);

/// <summary>The most frequent values of one facet field among the matches.</summary>
public sealed record SearchFacet(string Field, IReadOnlyList<SearchFacetValue> Values);

public sealed record SearchFacetValue(string Value, int Count);

public enum SearchStatus
{
    [LocalizedDescription("Answered", "تمت الإجابة")]
    Ok = 0,

    /// <summary>The index of this tenant has not been built yet (first deploy, new tenant).</summary>
    [LocalizedDescription("Index not ready", "الفهرس غير جاهز")]
    IndexNotReady = 1,

    /// <summary>The engine did not answer (down, timed out).</summary>
    [LocalizedDescription("Search engine unavailable", "محرك البحث غير متاح")]
    EngineUnavailable = 2
}

public enum SearchMatchMode
{
    /// <summary>No text: filters and sort only.</summary>
    [LocalizedDescription("Browse", "تصفح")]
    Browse = 0,

    /// <summary>The words matched.</summary>
    [LocalizedDescription("Exact match", "مطابقة تامة")]
    Exact = 1,

    /// <summary>Nothing matched exactly; these are close spellings.</summary>
    [LocalizedDescription("Close spelling", "تهجئة قريبة")]
    Fuzzy = 2
}
