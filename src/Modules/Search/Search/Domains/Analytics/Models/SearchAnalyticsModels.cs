namespace Search.Domains.Analytics.Models;

/// <summary>
/// Search counts of one index in one tenant on one UTC day (<c>search.search_daily_stats</c>). Written only by
/// <see cref="Services.SearchAnalyticsStore"/> (raw SQL, counts added to the row), read by the analytics queries,
/// removed after <c>Search:Analytics:RetentionDays</c>.
/// </summary>
public sealed class SearchDailyStats : IMustHaveTenant
{
    public Guid TenantId { get; set; }

    public string IndexKey { get; set; } = default!;

    public DateOnly Day { get; set; }

    /// <summary>Full searches with text (first page only).</summary>
    public int Searches { get; set; }

    public int ZeroResults { get; set; }

    /// <summary>Searches that found nothing exactly but close spellings.</summary>
    public int FuzzyRescues { get; set; }

    /// <summary>Searches the index could not answer (not built, engine down): the owner's SQL search answered them.</summary>
    public int Unanswered { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A search that found nothing, per index, tenant and UTC day (<c>search.search_zero_result_terms</c>). The term is the
/// normalized text, never tied to a user; see <see cref="Services.SearchQueryScrubber"/> for what is not kept.
/// </summary>
public sealed class SearchZeroResultTerm : IMustHaveTenant
{
    public const int TermMaxLength = 200;

    public Guid TenantId { get; set; }

    public string IndexKey { get; set; } = default!;

    public DateOnly Day { get; set; }

    public string Term { get; set; } = default!;

    public int Count { get; set; }

    public DateTime LastSeenAt { get; set; }
}
