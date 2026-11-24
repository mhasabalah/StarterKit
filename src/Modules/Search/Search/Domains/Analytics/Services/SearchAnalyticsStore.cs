using Npgsql;
using NpgsqlTypes;

namespace Search.Domains.Analytics.Services;

/// <summary>
/// Adds buffered counts to the analytics tables of the CURRENT tenant: one set-based upsert per table, where a row of the
/// same index, day (and term) gets the counts added. Raw SQL that names the tenant, on the context's connection (RLS).
/// Every replica flushes into the same rows, so the counts add up across replicas.
/// </summary>
public sealed class SearchAnalyticsStore(SearchDbContext db, ITenantContext tenantContext)
{
    private const string DailySql = """
        INSERT INTO search.search_daily_stats AS d
            (tenant_id, index_key, "day", searches, zero_results, fuzzy_rescues, unanswered, updated_at)
        SELECT @tenant, x.index_key, x.day, x.searches, x.zero_results, x.fuzzy_rescues, x.unanswered, @now
        FROM unnest(@index_keys, @days, @searches, @zero_results, @fuzzy_rescues, @unanswered)
             AS x(index_key, "day", searches, zero_results, fuzzy_rescues, unanswered)
        ON CONFLICT (tenant_id, index_key, "day") DO UPDATE SET
            searches = d.searches + EXCLUDED.searches,
            zero_results = d.zero_results + EXCLUDED.zero_results,
            fuzzy_rescues = d.fuzzy_rescues + EXCLUDED.fuzzy_rescues,
            unanswered = d.unanswered + EXCLUDED.unanswered,
            updated_at = EXCLUDED.updated_at
        """;

    private const string TermsSql = """
        INSERT INTO search.search_zero_result_terms AS t (tenant_id, index_key, "day", term, "count", last_seen_at)
        SELECT @tenant, x.index_key, x.day, x.term, x.count, x.last_seen_at
        FROM unnest(@index_keys, @days, @terms, @counts, @last_seen)
             AS x(index_key, "day", term, "count", last_seen_at)
        ON CONFLICT (tenant_id, index_key, "day", term) DO UPDATE SET
            "count" = t."count" + EXCLUDED."count",
            last_seen_at = GREATEST(t.last_seen_at, EXCLUDED.last_seen_at)
        """;

    /// <summary>Writes the counts of the current tenant (rows of other tenants are ignored).</summary>
    public async Task WriteAsync(
        IReadOnlyCollection<SearchDailyCounts> daily, IReadOnlyCollection<SearchZeroResultCount> terms, DateTime now,
        CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId
                       ?? throw new InvalidOperationException("Search analytics are written in a tenant scope.");
        var ownDaily = daily.Where(d => d.TenantId == tenantId).ToList();
        var ownTerms = terms.Where(t => t.TenantId == tenantId).ToList();

        if (ownDaily.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(DailySql,
            [
                Value("tenant", NpgsqlDbType.Uuid, tenantId),
                Value("now", NpgsqlDbType.TimestampTz, now),
                Array("index_keys", NpgsqlDbType.Text, ownDaily.Select(d => d.IndexKey)),
                Array("days", NpgsqlDbType.Date, ownDaily.Select(d => d.Day)),
                Array("searches", NpgsqlDbType.Integer, ownDaily.Select(d => d.Searches)),
                Array("zero_results", NpgsqlDbType.Integer, ownDaily.Select(d => d.ZeroResults)),
                Array("fuzzy_rescues", NpgsqlDbType.Integer, ownDaily.Select(d => d.FuzzyRescues)),
                Array("unanswered", NpgsqlDbType.Integer, ownDaily.Select(d => d.Unanswered))
            ], cancellationToken);
        }

        if (ownTerms.Count > 0)
        {
            await db.Database.ExecuteSqlRawAsync(TermsSql,
            [
                Value("tenant", NpgsqlDbType.Uuid, tenantId),
                Array("index_keys", NpgsqlDbType.Text, ownTerms.Select(t => t.IndexKey)),
                Array("days", NpgsqlDbType.Date, ownTerms.Select(t => t.Day)),
                Array("terms", NpgsqlDbType.Text, ownTerms.Select(t => t.Term)),
                Array("counts", NpgsqlDbType.Integer, ownTerms.Select(t => t.Count)),
                Array("last_seen", NpgsqlDbType.TimestampTz, ownTerms.Select(t => t.LastSeenAt))
            ], cancellationToken);
        }
    }

    private static NpgsqlParameter Value(string name, NpgsqlDbType type, object value) =>
        new(name, type) { Value = value };

    private static NpgsqlParameter Array<T>(string name, NpgsqlDbType type, IEnumerable<T> values) =>
        new(name, NpgsqlDbType.Array | type) { Value = values.ToArray() };
}
