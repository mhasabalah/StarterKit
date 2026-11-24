using Microsoft.Extensions.Options;

namespace Search.Domains.Analytics.Services;

/// <summary>What one search contributes to the analytics.</summary>
public enum SearchOutcome
{
    /// <summary>The index answered with results.</summary>
    Found = 0,

    /// <summary>The index answered with nothing.</summary>
    ZeroResults = 1,

    /// <summary>Nothing matched exactly; close spellings were found.</summary>
    FuzzyRescue = 2,

    /// <summary>The index could not answer (not built, engine down).</summary>
    Unanswered = 3
}

/// <summary>Counts of one index in one tenant on one day, as buffered between two flushes.</summary>
public sealed record SearchDailyCounts(Guid TenantId, string IndexKey, DateOnly Day)
{
    public int Searches { get; set; }
    public int ZeroResults { get; set; }
    public int FuzzyRescues { get; set; }
    public int Unanswered { get; set; }
}

/// <summary>A zero-result term as buffered between two flushes.</summary>
public sealed record SearchZeroResultCount(Guid TenantId, string IndexKey, DateOnly Day, string Term)
{
    public int Count { get; set; }
    public DateTime LastSeenAt { get; set; }
}

/// <summary>Everything buffered since the last flush.</summary>
public sealed record SearchAnalyticsBatch(
    IReadOnlyList<SearchDailyCounts> Daily, IReadOnlyList<SearchZeroResultCount> ZeroResults, int DroppedTerms)
{
    public bool IsEmpty => Daily.Count == 0 && ZeroResults.Count == 0;
}

/// <summary>
/// Per-replica, in-memory counts of searches between two flushes (<see cref="SearchAnalyticsFlushService"/>), so a search
/// never waits for an analytics write. Bounded: at most <c>Search:Analytics:MaxPendingTerms</c> distinct zero-result
/// terms are held; more are dropped (and counted) until the next flush. The daily counts are always kept (one entry per
/// tenant, index and day). A replica that stops without a final flush loses at most one interval: analytics are
/// best effort.
/// </summary>
public sealed class SearchAnalyticsBuffer(IOptions<SearchOptions> options)
{
    private readonly Lock _gate = new();
    private Dictionary<(Guid, string, DateOnly), SearchDailyCounts> _daily = [];
    private Dictionary<(Guid, string, DateOnly, string), SearchZeroResultCount> _terms = [];
    private int _droppedTerms;

    /// <summary>Records one search. <paramref name="term"/> is the scrubbed text (only kept for zero results).</summary>
    public void Record(Guid tenantId, string indexKey, SearchOutcome outcome, string? term, DateTime now)
    {
        var day = DateOnly.FromDateTime(now);
        lock (_gate)
        {
            if (!_daily.TryGetValue((tenantId, indexKey, day), out var daily))
                _daily[(tenantId, indexKey, day)] = daily = new SearchDailyCounts(tenantId, indexKey, day);

            daily.Searches++;
            switch (outcome)
            {
                case SearchOutcome.ZeroResults:
                    daily.ZeroResults++;
                    break;
                case SearchOutcome.FuzzyRescue:
                    daily.FuzzyRescues++;
                    break;
                case SearchOutcome.Unanswered:
                    daily.Unanswered++;
                    break;
            }

            if (outcome != SearchOutcome.ZeroResults || term is null)
                return;

            var key = (tenantId, indexKey, day, term);
            if (!_terms.TryGetValue(key, out var count))
            {
                if (_terms.Count >= options.Value.Analytics.MaxPendingTerms)
                {
                    _droppedTerms++;
                    return;
                }

                _terms[key] = count = new SearchZeroResultCount(tenantId, indexKey, day, term);
            }

            count.Count++;
            count.LastSeenAt = now;
        }
    }

    /// <summary>Takes everything buffered so far and starts empty.</summary>
    public SearchAnalyticsBatch Drain()
    {
        lock (_gate)
        {
            var batch = new SearchAnalyticsBatch([.. _daily.Values], [.. _terms.Values], _droppedTerms);
            _daily = [];
            _terms = [];
            _droppedTerms = 0;
            return batch;
        }
    }
}
