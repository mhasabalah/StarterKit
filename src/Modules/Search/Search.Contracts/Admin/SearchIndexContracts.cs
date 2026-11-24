namespace Search.Contracts.Admin;

/// <summary>Where an index of one tenant stands in its build cycle.</summary>
public enum SearchIndexStatus
{
    /// <summary>Never built: searches answer <c>IndexNotReady</c> and owners use their SQL search.</summary>
    [LocalizedDescription("Not built", "لم يُبنَ بعد")]
    NotBuilt = 0,

    /// <summary>A rebuild holds the lease. A previous build, if any, keeps answering.</summary>
    [LocalizedDescription("Building", "قيد البناء")]
    Building = 1,

    [LocalizedDescription("Ready", "جاهز")]
    Ready = 2,

    /// <summary>The first build failed; nothing can answer yet.</summary>
    [LocalizedDescription("Failed", "فشل")]
    Failed = 3
}

/// <summary>The registered indexes with their build state in the CURRENT tenant (tenant permission <c>search.manage</c>).</summary>
public sealed record GetSearchIndexesQuery : IQuery<List<SearchIndexDto>>;

/// <summary>One index with its build state in the CURRENT tenant; 404 when no module registers <paramref name="IndexKey"/>.</summary>
public sealed record GetSearchIndexByKeyQuery(string IndexKey) : IQuery<SearchIndexDto>;

/// <summary>
/// Queues a full rebuild of one index of the CURRENT tenant on the <c>search</c> queue, even when it is up to date.
/// The current build keeps answering until the new one is complete. 409 <c>search_rebuild_running</c> while a rebuild
/// holds the lease.
/// </summary>
public sealed record RebuildSearchIndexCommand(string IndexKey) : ICommand<RebuildSearchIndexResult>;

public sealed record RebuildSearchIndexResult(string IndexKey);

/// <summary>
/// Queues a drift check of one index of the CURRENT tenant: the documents missing from the index, indexed with an old
/// version, or gone from the source (repaired with <c>Search:Drift:AutoRepair</c>). The findings appear on the index
/// (<see cref="SearchIndexDto.Drift"/>). 409 <c>search_index_not_built</c> before the first build.
/// </summary>
public sealed record CheckSearchIndexDriftCommand(string IndexKey) : ICommand<CheckSearchIndexDriftResult>;

public sealed record CheckSearchIndexDriftResult(string IndexKey);

/// <summary>An index of the current tenant.</summary>
public sealed record SearchIndexDto
{
    public required string Key { get; init; }

    public required LocalizedText Name { get; init; }

    public SearchIndexStatus Status { get; init; }

    /// <summary>The live documents were built from the current definition (otherwise a rebuild is due).</summary>
    public bool UpToDate { get; init; }

    /// <summary>Documents of the last completed build (live changes since then are not counted).</summary>
    public int DocumentCount { get; init; }

    public DateTime? LastBuiltAt { get; init; }

    /// <summary>When the rebuild in progress (or the last one) started.</summary>
    public DateTime? BuildStartedAt { get; init; }

    /// <summary>Why the last rebuild failed (for example <c>source_failed</c>); <c>null</c> after a successful one.</summary>
    public string? LastErrorCode { get; init; }

    /// <summary>The last drift check, <c>null</c> before the first one.</summary>
    public SearchIndexDriftDto? Drift { get; init; }

    public IReadOnlyList<SearchTextFieldDto> TextFields { get; init; } = [];

    public IReadOnlyList<SearchFilterFieldDto> FilterFields { get; init; } = [];
}

/// <summary>What the last drift check found (before any repair).</summary>
public sealed record SearchIndexDriftDto(DateTime CheckedAt, int Missing, int Stale, int Orphaned);

public sealed record SearchTextFieldDto(string Name, SearchWeight Weight, SearchAnalyzer Analyzer);

public sealed record SearchFilterFieldDto(string Name, SearchFieldType Type, bool Facetable, bool Sortable);
