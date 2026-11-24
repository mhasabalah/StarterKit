namespace Search.Contracts.Admin;

/// <summary>The configured engine and every registered index definition (platform permission <c>search.operate</c>).</summary>
public sealed record GetSearchEngineQuery : IQuery<SearchEngineDto>;

/// <param name="Engine">The <c>Search:Engine</c> setting (<c>Postgres</c>, <c>Meilisearch</c>).</param>
public sealed record SearchEngineDto(string Engine, IReadOnlyList<SearchIndexDefinitionDto> Indexes);

/// <param name="Fingerprint">Changes with any field, weight, analyzer or the text normalizer: every tenant then rebuilds.</param>
public sealed record SearchIndexDefinitionDto(
    string Key,
    LocalizedText Name,
    string Fingerprint,
    IReadOnlyList<SearchTextFieldDto> TextFields,
    IReadOnlyList<SearchFilterFieldDto> FilterFields);

/// <summary>
/// Queues a full rebuild of one index in EVERY tenant (platform permission <c>search.operate</c>): one rebuild job per
/// tenant on the <c>search</c> queue, each serving its previous build until it completes. For a changed engine or a
/// repair after an incident; definition changes are picked up by the reconcile job on their own.
/// </summary>
public sealed record RebuildSearchIndexInAllTenantsCommand(string IndexKey) : ICommand<RebuildSearchIndexInAllTenantsResult>;

public sealed record RebuildSearchIndexInAllTenantsResult(string IndexKey);
