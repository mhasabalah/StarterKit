using System.Diagnostics.Metrics;

namespace Search.Observability;

/// <summary>
/// Meter <see cref="MeterName"/>:
/// <list type="bullet">
/// <item><c>search.queries</c> by <c>index</c>, <c>status</c> and <c>match_mode</c>; alert on <c>engine_unavailable</c>;</item>
/// <item><c>search.query.duration</c> (ms) by <c>index</c>;</item>
/// <item><c>search.documents.indexed</c> by <c>index</c> and <c>operation</c> (upsert, delete);</item>
/// <item><c>search.index.lag</c> (s): from the change to its indexing, by <c>index</c>;</item>
/// <item><c>search.rebuilds</c> by <c>index</c> and <c>outcome</c>;</item>
/// <item><c>search.drift.documents</c> by <c>index</c> and <c>kind</c> (missing, stale, orphaned);</item>
/// <item><c>search.analytics.terms_dropped</c>: zero-result terms not kept because the buffer was full.</item>
/// </list>
/// Tags never carry query text, tenant ids or document ids.
/// </summary>
public sealed class SearchMetrics
{
    public const string MeterName = "StarterKit.Search";

    private readonly Counter<long> _queries;
    private readonly Histogram<double> _queryDuration;
    private readonly Counter<long> _indexed;
    private readonly Histogram<double> _lag;
    private readonly Counter<long> _rebuilds;
    private readonly Counter<long> _drift;
    private readonly Counter<long> _termsDropped;

    public SearchMetrics(IMeterFactory? meterFactory = null)
    {
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _queries = meter.CreateCounter<long>("search.queries", description: "Searches, by index, status and match mode");
        _queryDuration = meter.CreateHistogram<double>("search.query.duration", unit: "ms", description: "Search duration");
        _indexed = meter.CreateCounter<long>("search.documents.indexed", description: "Documents written to or removed from an index");
        _lag = meter.CreateHistogram<double>("search.index.lag", unit: "s", description: "Time from a change to its indexing");
        _rebuilds = meter.CreateCounter<long>("search.rebuilds", description: "Index rebuilds, by outcome");
        _drift = meter.CreateCounter<long>("search.drift.documents", description: "Documents found out of sync by the drift check");
        _termsDropped = meter.CreateCounter<long>("search.analytics.terms_dropped",
            description: "Zero-result terms not kept because the analytics buffer was full");
    }

    public void Queried(string index, SearchStatus status, SearchMatchMode mode, double milliseconds)
    {
        _queries.Add(1, Tag("index", index), Tag("status", Snake(status.ToString())), Tag("match_mode", Snake(mode.ToString())));
        _queryDuration.Record(milliseconds, Tag("index", index));
    }

    public void Indexed(string index, int upserted, int deleted)
    {
        if (upserted > 0)
            _indexed.Add(upserted, Tag("index", index), Tag("operation", "upsert"));
        if (deleted > 0)
            _indexed.Add(deleted, Tag("index", index), Tag("operation", "delete"));
    }

    public void Lag(string index, TimeSpan lag) => _lag.Record(Math.Max(0, lag.TotalSeconds), Tag("index", index));

    public void Rebuilt(string index, string outcome) => _rebuilds.Add(1, Tag("index", index), Tag("outcome", outcome));

    public void Drift(string index, int missing, int stale, int orphaned)
    {
        if (missing > 0)
            _drift.Add(missing, Tag("index", index), Tag("kind", "missing"));
        if (stale > 0)
            _drift.Add(stale, Tag("index", index), Tag("kind", "stale"));
        if (orphaned > 0)
            _drift.Add(orphaned, Tag("index", index), Tag("kind", "orphaned"));
    }

    public void AnalyticsTermsDropped(int count) => _termsDropped.Add(count);

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);

    private static string Snake(string pascal) =>
        string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
