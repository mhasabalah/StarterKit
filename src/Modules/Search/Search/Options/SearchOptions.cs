namespace Search.Options;

/// <summary>The <c>Search</c> configuration section (validated at startup by <see cref="SearchOptionsValidator"/>).</summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>The Hangfire queue of indexing work (rebuilds, drift checks); see <c>Hangfire:Servers:Search</c>.</summary>
    public const string Queue = "search";

    /// <summary>The engine that stores and answers the index. Changing it needs a rebuild of every index.</summary>
    public SearchEngineKind Engine { get; set; } = SearchEngineKind.Postgres;

    public SearchIndexingOptions Indexing { get; set; } = new();

    public SearchQueryOptions Query { get; set; } = new();

    public SearchFacetOptions Facets { get; set; } = new();

    public SearchPostgresOptions Postgres { get; set; } = new();

    public SearchMeilisearchOptions Meilisearch { get; set; } = new();

    public SearchReconcileOptions Reconcile { get; set; } = new();

    public SearchDriftOptions Drift { get; set; } = new();

    public SearchAnalyticsOptions Analytics { get; set; } = new();
}

/// <summary>The engines Search can run on. Adding one = one value, one adapter, the conformance suite (ADR-0036).</summary>
public enum SearchEngineKind
{
    /// <summary>The application's PostgreSQL database (default; no extra infrastructure).</summary>
    Postgres = 0,

    /// <summary>A Meilisearch server (<c>Search:Meilisearch</c>, <c>docker-compose.meilisearch.yml</c>).</summary>
    Meilisearch = 1
}

public sealed class SearchIndexingOptions
{
    /// <summary>Documents per batch during a rebuild or a repair.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>How long a rebuild holds its lease before another worker may take it over.</summary>
    public int LeaseMinutes { get; set; } = 30;
}

public sealed class SearchQueryOptions
{
    public int MaxPageSize { get; set; } = 100;

    /// <summary>The deepest result a query can page to (page × page size).</summary>
    public int MaxWindow { get; set; } = 1000;

    public int MaxTextLength { get; set; } = 200;

    /// <summary>Words of the search text beyond this are ignored.</summary>
    public int MaxTerms { get; set; } = 10;

    public SearchFuzzyOptions Fuzzy { get; set; } = new();
}

public sealed class SearchFuzzyOptions
{
    /// <summary>Look for close spellings when nothing matches exactly.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The text must have a word at least this long for the fuzzy fallback.</summary>
    public int MinTermLength { get; set; } = 3;

    /// <summary>Trigram similarity a fuzzy match needs, 0 to 1 (PostgreSQL engine).</summary>
    public double Threshold { get; set; } = 0.4;
}

public sealed class SearchFacetOptions
{
    /// <summary>Values returned per facet, most frequent first.</summary>
    public int MaxValues { get; set; } = 20;

    /// <summary>Matches counted for facets; above it the counts are partial (<c>FacetsExhaustive = false</c>).</summary>
    public int ScanLimit { get; set; } = 10_000;
}

public sealed class SearchPostgresOptions
{
    /// <summary>Statement timeout of every search query.</summary>
    public int StatementTimeoutMs { get; set; } = 2000;
}

public sealed class SearchMeilisearchOptions
{
    /// <summary>The server, for example <c>http://meilisearch:7700</c>. Required with <c>Engine=Meilisearch</c>.</summary>
    public string? Url { get; set; }

    /// <summary>An admin API key (not the master key), from a Docker secret or the environment.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Prefix of the Meilisearch index names, so several deployments can share one server.</summary>
    public string IndexPrefix { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>How long an indexing call waits for its Meilisearch task to finish.</summary>
    public int TaskTimeoutSeconds { get; set; } = 30;
}

public sealed class SearchReconcileOptions
{
    /// <summary>Rebuilds indexes that are missing or whose definition changed, in every tenant.</summary>
    public string Cron { get; set; } = "*/15 * * * *";
}

public sealed class SearchDriftOptions
{
    /// <summary>Compares every index with its source.</summary>
    public string Cron { get; set; } = "0 3 * * *";

    /// <summary>Re-index missing and stale documents and remove orphaned ones; <c>false</c> only reports them.</summary>
    public bool AutoRepair { get; set; } = true;

    /// <summary>Indexes larger than this are skipped by the drift check (with a warning).</summary>
    public int MaxDocuments { get; set; } = 200_000;
}

public sealed class SearchAnalyticsOptions
{
    /// <summary>Count searches and remember the ones that found nothing (no user ids or IP addresses).</summary>
    public bool Enabled { get; set; } = true;

    public int RetentionDays { get; set; } = 90;

    /// <summary>Longer zero-result queries are cut to this length.</summary>
    public int MaxQueryLength { get; set; } = 64;

    /// <summary>How often each replica writes its counts.</summary>
    public int FlushSeconds { get; set; } = 30;

    /// <summary>Distinct zero-result terms a replica holds between two flushes; more are dropped until the next flush.</summary>
    public int MaxPendingTerms { get; set; } = 10_000;

    /// <summary>Removes the analytics older than <see cref="RetentionDays"/> (also when analytics are off).</summary>
    public string PurgeCron { get; set; } = "30 3 * * *";
}
