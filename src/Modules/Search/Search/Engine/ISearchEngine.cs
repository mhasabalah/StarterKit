using Search.Engine.Text;

namespace Search.Engine;

/// <summary>
/// The port every search engine implements (ADR-0036). Engines store and match prepared documents; everything
/// engine-neutral (validation, normalization, query parsing, highlighting, index state) happens outside them, so an
/// adapter is only storage and matching. Every adapter must pass the engine conformance suite.
/// </summary>
/// <remarks>
/// Every call names its <see cref="IndexScope"/>: an engine never reads or writes outside that tenant, and applies the
/// branch scope to every query itself.
/// </remarks>
public interface ISearchEngine
{
    SearchEngineCapabilities Capabilities { get; }

    /// <summary>Creates or updates the engine's index for <paramref name="index"/> (idempotent).</summary>
    Task EnsureIndexAsync(RegisteredIndex index, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces documents. With <see cref="SearchEngineCapabilities.VersionGuard"/>, an older version never replaces a newer one.</summary>
    Task UpsertAsync(IndexScope scope, IReadOnlyList<EngineDocument> documents, long stamp, CancellationToken cancellationToken);

    /// <summary>Removes documents (unknown ids are ignored).</summary>
    Task DeleteAsync(IndexScope scope, IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

    /// <summary>
    /// After a rebuild: removes the documents written before it started that it did not reach (stamp below
    /// <paramref name="stamp"/> and indexed before <paramref name="startedAt"/>). Returns how many.
    /// </summary>
    Task<int> SweepAsync(IndexScope scope, long stamp, DateTime startedAt, CancellationToken cancellationToken);

    Task<EngineSearchResult> SearchAsync(IndexScope scope, EngineQuery query, CancellationToken cancellationToken);

    Task<int> CountAsync(IndexScope scope, CancellationToken cancellationToken);

    /// <summary>The id and version of every document of the scope (drift detection).</summary>
    IAsyncEnumerable<SearchDocumentStamp> StreamStampsAsync(IndexScope scope, CancellationToken cancellationToken);

    /// <summary>Removes every document of a tenant, in every index (tenant purge).</summary>
    Task DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>What an engine can do natively; tests and docs use it instead of hiding differences.</summary>
/// <param name="Stemming">Word forms match ("running" finds "run").</param>
/// <param name="VersionGuard">An older document version never replaces a newer one.</param>
public sealed record SearchEngineCapabilities(string Name, bool Stemming, bool VersionGuard);

/// <summary>One index of one tenant, within the reader's branches (<c>null</c> = all branches).</summary>
public sealed record IndexScope(RegisteredIndex Index, Guid TenantId, IReadOnlyList<Guid>? AllowedBranchIds = null);

/// <summary>A document prepared for the engines: validated, cut to length and normalized.</summary>
public sealed record EngineDocument(
    string Id,
    Guid? BranchId,
    long Version,
    IReadOnlyDictionary<string, string> Text,
    string SuggestText,
    IReadOnlyDictionary<string, SearchValue> Filters,
    IReadOnlyDictionary<string, string> HighlightOriginals);

/// <summary>A search prepared for the engines: the text is parsed, the request validated.</summary>
public sealed record EngineQuery
{
    public required ParsedSearchQuery Text { get; init; }

    public IReadOnlyList<SearchFilter> Filters { get; init; } = [];

    public IReadOnlyList<string> Facets { get; init; } = [];

    /// <summary><c>null</c>: relevance with text, the index's default sort without.</summary>
    public SearchSort? Sort { get; init; }

    public int Offset { get; init; }

    public int Limit { get; init; } = 20;

    /// <summary>Return the original text of the highlight fields with each hit.</summary>
    public bool IncludeHighlightText { get; init; }

    /// <summary>When nothing matches, try close spellings of words at least this long; <c>null</c> = off.</summary>
    public int? FuzzyMinTermLength { get; init; }

    public double FuzzyThreshold { get; init; } = 0.4;

    public int MaxFacetValues { get; init; } = 20;

    public int FacetScanLimit { get; init; } = 10_000;
}

public sealed record EngineSearchResult(
    IReadOnlyList<EngineHit> Hits,
    int Total,
    IReadOnlyList<SearchFacet> Facets,
    bool FacetsExhaustive,
    SearchMatchMode MatchMode)
{
    public static readonly EngineSearchResult None = new([], 0, [], true, SearchMatchMode.Exact);
}

/// <param name="HighlightText">The original text of the highlight fields (when requested).</param>
public sealed record EngineHit(string Id, double Score, IReadOnlyDictionary<string, string> HighlightText);
