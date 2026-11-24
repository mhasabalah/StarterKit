using System.Globalization;
using System.Text;
using static Search.Engine.Meilisearch.MeilisearchNames;

namespace Search.Engine.Meilisearch;

/// <summary>
/// Compiles the scope and the query filters into one Meilisearch filter expression. The tenant (and the reader's
/// branch scope) is ALWAYS the first condition: one shared index serves every tenant, so isolation lives here. Values
/// are always quoted and escaped; attribute names come from validated definitions (lower snake case) and the adapter's
/// own constants, never from callers.
/// </summary>
internal static class MeilisearchFilterCompiler
{
    public static string Scope(IndexScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var tenant = $"{Tenant} = {Quote(TenantValue(scope.TenantId))}";
        return scope.AllowedBranchIds switch
        {
            null => tenant,
            [] => $"{tenant} AND {Branch} IS NULL",
            var branches => $"{tenant} AND ({Branch} IS NULL OR {Branch} IN {List(branches.Select(b => b.ToString()))})"
        };
    }

    /// <summary>The documents a rebuild with <paramref name="stamp"/> did not reach (see <c>ISearchEngine.SweepAsync</c>).</summary>
    public static string Sweep(IndexScope scope, long stamp, DateTime startedAt) =>
        $"{Tenant} = {Quote(TenantValue(scope.TenantId))} AND {Stamp} < {stamp.ToString(CultureInfo.InvariantCulture)} " +
        $"AND {IndexedAt} < {MeilisearchDocuments.EpochMilliseconds(startedAt).ToString(CultureInfo.InvariantCulture)}";

    public static string ForTenant(Guid tenantId) => $"{Tenant} = {Quote(TenantValue(tenantId))}";

    public static string Compile(IndexScope scope, IReadOnlyList<SearchFilter> filters)
    {
        var expression = new StringBuilder(Scope(scope));
        foreach (var filter in filters)
            expression.Append(" AND ").Append(Condition(filter));
        return expression.ToString();
    }

    /// <summary>A double-quoted Meilisearch string: backslashes and quotes escaped.</summary>
    public static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Condition(SearchFilter filter)
    {
        var field = FilterAttribute(filter.Field);
        return filter switch
        {
            KeywordFilter k => $"{field} = {Quote(k.Value)}",
            KeywordsFilter { Match: SearchMatch.All } k => "(" + string.Join(" AND ", k.Values.Select(v => $"{field} = {Quote(v)}")) + ")",
            KeywordsFilter k => $"{field} IN {List(k.Values)}",
            NumberRangeFilter n => Range(field, n.Min?.ToString(CultureInfo.InvariantCulture), n.Max?.ToString(CultureInfo.InvariantCulture)),
            DateRangeFilter d => Range(field,
                d.From is { } from ? MeilisearchDocuments.EpochMilliseconds(from).ToString(CultureInfo.InvariantCulture) : null,
                d.To is { } to ? MeilisearchDocuments.EpochMilliseconds(to).ToString(CultureInfo.InvariantCulture) : null),
            BoolFilter b => $"{field} = {(b.Value ? "true" : "false")}",
            _ => throw new InvalidOperationException($"Unsupported search filter {filter.GetType().Name}.")
        };
    }

    private static string Range(string field, string? min, string? max) => (min, max) switch
    {
        (null, null) => $"{field} EXISTS",
        ({ } low, null) => $"{field} >= {low}",
        (null, { } high) => $"{field} <= {high}",
        ({ } low, { } high) => $"({field} >= {low} AND {field} <= {high})"
    };

    private static string List(IEnumerable<string> values) => "[" + string.Join(", ", values.Select(Quote)) + "]";
}
