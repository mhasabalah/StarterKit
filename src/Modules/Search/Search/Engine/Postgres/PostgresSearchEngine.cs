using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Search.Engine.Postgres;

/// <summary>
/// The default engine: the application's own PostgreSQL (ADR-0036). Documents live in <c>search.search_documents</c>;
/// the <c>tsvector</c> is built by PostgreSQL from the normalized text with each field's weight and configurations;
/// matching is full text first, then (when nothing matches) trigram similarity on the suggest text.
/// </summary>
/// <remarks>
/// Every statement runs on the context's own connection, so the tenant session settings of row-level security apply,
/// AND names the tenant and the index explicitly (raw SQL gets no EF query filters). Searches run in a short
/// transaction with a statement timeout.
/// </remarks>
public sealed class PostgresSearchEngine(SearchDbContext db, IOptions<SearchOptions> options, TimeProvider time) : ISearchEngine
{
    private static readonly ConcurrentDictionary<string, PostgresIndexSql> SqlByIndex = new(StringComparer.Ordinal);

    public SearchEngineCapabilities Capabilities { get; } = new("Postgres", Stemming: true, VersionGuard: true);

    public Task EnsureIndexAsync(RegisteredIndex index, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task UpsertAsync(IndexScope scope, IReadOnlyList<EngineDocument> documents, long stamp, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
            return;

        // ON CONFLICT cannot touch a row twice in one statement: keep the newest version of each id.
        var batch = documents.GroupBy(d => d.Id, StringComparer.Ordinal)
            .Select(g => g.MaxBy(d => d.Version)!)
            .ToList();
        var sql = For(scope.Index);

        await RunAsync(searchSettings: false, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(sql.UpsertSql, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = scope.TenantId });
            command.Parameters.Add(new NpgsqlParameter("index", NpgsqlDbType.Text) { Value = scope.Index.Key });
            command.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlDbType.Bigint) { Value = stamp });
            command.Parameters.Add(new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = time.GetUtcNow().UtcDateTime });
            AddArray(command, "ids", NpgsqlDbType.Text, batch.Select(d => d.Id).ToArray());
            AddArray(command, "branches", NpgsqlDbType.Uuid, batch.Select(d => d.BranchId).ToArray());
            AddArray(command, "versions", NpgsqlDbType.Bigint, batch.Select(d => d.Version).ToArray());
            AddArray(command, "suggest", NpgsqlDbType.Text, batch.Select(d => d.SuggestText).ToArray());
            AddArray(command, "filters", NpgsqlDbType.Text, batch.Select(d => PostgresIndexSql.FiltersJson(d.Filters)).ToArray());
            AddArray(command, "content", NpgsqlDbType.Text, batch.Select(d => PostgresIndexSql.ObjectJson(d.HighlightOriginals)).ToArray());
            for (var i = 0; i < sql.TextFields.Count; i++)
            {
                var field = sql.TextFields[i].Name;
                AddArray(command, $"t{i}", NpgsqlDbType.Text, batch.Select(d => d.Text.GetValueOrDefault(field)).ToArray());
            }

            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task DeleteAsync(IndexScope scope, IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
            return;

        await ExecuteAsync(
            $"DELETE FROM {PostgresIndexSql.Table} WHERE tenant_id = @tenant AND index_key = @index AND document_id = ANY(@ids)",
            scope, command => AddArray(command, "ids", NpgsqlDbType.Text, ids.ToArray()), cancellationToken);
    }

    public Task<int> SweepAsync(IndexScope scope, long stamp, DateTime startedAt, CancellationToken cancellationToken) =>
        ExecuteAsync(
            $"DELETE FROM {PostgresIndexSql.Table} WHERE tenant_id = @tenant AND index_key = @index " +
            "AND rebuild_stamp < @stamp AND indexed_at < @startedAt",
            scope,
            command =>
            {
                command.Parameters.Add(new NpgsqlParameter("stamp", NpgsqlDbType.Bigint) { Value = stamp });
                command.Parameters.Add(new NpgsqlParameter("startedAt", NpgsqlDbType.TimestampTz) { Value = startedAt });
            },
            cancellationToken);

    public async Task<int> CountAsync(IndexScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return await RunAsync(searchSettings: false, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(
                $"SELECT count(*) FROM {PostgresIndexSql.Table} WHERE tenant_id = @tenant AND index_key = @index",
                connection, transaction);
            AddScope(command, scope);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }, cancellationToken);
    }

    public async IAsyncEnumerable<SearchDocumentStamp> StreamStampsAsync(
        IndexScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var opened = await OpenAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                $"SELECT document_id, source_version FROM {PostgresIndexSql.Table} " +
                "WHERE tenant_id = @tenant AND index_key = @index ORDER BY document_id",
                Connection, CurrentTransaction);
            AddScope(command, scope);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                yield return new SearchDocumentStamp(reader.GetString(0), reader.GetInt64(1));
        }
        finally
        {
            if (opened)
                await db.Database.CloseConnectionAsync();
        }
    }

    public async Task DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await RunAsync(searchSettings: false, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(
                $"DELETE FROM {PostgresIndexSql.Table} WHERE tenant_id = @tenant", connection, transaction);
            command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = tenantId });
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);

    public async Task<EngineSearchResult> SearchAsync(IndexScope scope, EngineQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        var sql = For(scope.Index);

        return await RunAsync(searchSettings: true, async (connection, transaction) =>
        {
            if (query.Text.IsEmpty)
                return await QueryAsync(connection, transaction, scope, sql, query, Match.Browse, cancellationToken);

            var exact = await QueryAsync(connection, transaction, scope, sql, query, Match.FullText, cancellationToken);
            var fuzzy = exact.Total == 0
                        && query.FuzzyMinTermLength is { } minLength
                        && scope.Index.Definition.TextFields.Any(f => f.Suggest)
                        && query.Text.PositiveWords.Any(w => w.Length >= minLength);
            return fuzzy
                ? await QueryAsync(connection, transaction, scope, sql, query, Match.Trigram, cancellationToken)
                : exact;
        }, cancellationToken, query.FuzzyThreshold);
    }

    // ----- queries ---------------------------------------------------------------------------------------------------

    private enum Match
    {
        Browse,
        FullText,
        Trigram
    }

    /// <summary>The FROM/WHERE of one search, with its own parameters (a command's parameters cannot be shared).</summary>
    private sealed record Plan(SqlParameters Parameters, string With, string From, string Where, string Score, string Order);

    private static Plan BuildPlan(IndexScope scope, PostgresIndexSql sql, EngineQuery query, Match match)
    {
        var p = new SqlParameters();
        var branches = p.Add(scope.AllowedBranchIds?.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Uuid);
        var where = new List<string>
        {
            $"d.tenant_id = {p.Add(scope.TenantId, NpgsqlDbType.Uuid)}",
            $"d.index_key = {p.Add(scope.Index.Key, NpgsqlDbType.Text)}",
            $"({branches} IS NULL OR d.branch_id IS NULL OR d.branch_id = ANY({branches}))"
        };
        where.AddRange(PostgresFilterCompiler.Compile(scope.Index, query.Filters, p));

        var with = "";
        var from = $"{PostgresIndexSql.Table} d";
        var score = "0::float8";
        switch (match)
        {
            case Match.FullText:
                with = $"q AS (SELECT {PostgresTsQueryCompiler.Compile(query.Text, sql.QueryConfigurations, p)} AS tsq)";
                from += ", q";
                where.Add("d.search_vector @@ q.tsq");
                score = $"ts_rank_cd({PostgresIndexSql.RankWeights}, d.search_vector, q.tsq, 1)::float8";
                break;
            case Match.Trigram:
                var words = p.Add(string.Join(' ', query.Text.PositiveWords), NpgsqlDbType.Text);
                where.Add($"{words} <% d.suggest_text");
                score = $"word_similarity({words}, d.suggest_text)::float8";
                break;
        }

        string order;
        if (query.Sort is { } sort)
            order = PostgresFilterCompiler.SortExpression(scope.Index.FilterFields[sort.Field], sort.Descending, p);
        else if (match != Match.Browse)
            order = "score DESC";
        else if (scope.Index.Definition.DefaultSort is { } defaultSort)
            order = PostgresFilterCompiler.SortExpression(scope.Index.FilterFields[defaultSort.Field], defaultSort.Descending, p);
        else
            order = "";
        order = order.Length == 0 ? "d.document_id" : order + ", d.document_id";

        return new Plan(p, with, from, string.Join(" AND ", where), score, order);
    }

    private static async Task<EngineSearchResult> QueryAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, IndexScope scope, PostgresIndexSql sql,
        EngineQuery query, Match match, CancellationToken cancellationToken)
    {
        var plan = BuildPlan(scope, sql, query, match);
        var limit = plan.Parameters.Add(query.Limit, NpgsqlDbType.Integer);
        var offset = plan.Parameters.Add(query.Offset, NpgsqlDbType.Integer);
        var content = query.IncludeHighlightText ? ", d.content::text" : ", NULL::text";

        var hits = new List<EngineHit>();
        var total = 0;
        await using (var command = new NpgsqlCommand(
                         $"{With(plan.With)}SELECT d.document_id, {plan.Score} AS score, count(*) OVER () AS total{content} " +
                         $"FROM {plan.From} WHERE {plan.Where} ORDER BY {plan.Order} LIMIT {limit} OFFSET {offset}",
                         connection, transaction))
        {
            plan.Parameters.CopyTo(command);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                total = (int)reader.GetInt64(2);
                hits.Add(new EngineHit(reader.GetString(0), reader.GetDouble(1),
                    reader.IsDBNull(3) ? EmptyText : ParseObject(reader.GetString(3))));
            }
        }

        if (hits.Count == 0 && query.Offset > 0)
            total = await CountMatchesAsync(connection, transaction, scope, sql, query, match, cancellationToken);

        var facets = query.Facets.Count > 0 && total > 0
            ? await FacetsAsync(connection, transaction, scope, sql, query, match, cancellationToken)
            : [];

        var mode = match switch
        {
            Match.Browse => SearchMatchMode.Browse,
            Match.Trigram => SearchMatchMode.Fuzzy,
            _ => SearchMatchMode.Exact
        };
        return new EngineSearchResult(hits, total, facets, total <= query.FacetScanLimit, mode);
    }

    private static async Task<int> CountMatchesAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, IndexScope scope, PostgresIndexSql sql,
        EngineQuery query, Match match, CancellationToken cancellationToken)
    {
        var plan = BuildPlan(scope, sql, query, match);
        await using var command = new NpgsqlCommand(
            $"{With(plan.With)}SELECT count(*) FROM {plan.From} WHERE {plan.Where}", connection, transaction);
        plan.Parameters.CopyTo(command);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<IReadOnlyList<SearchFacet>> FacetsAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, IndexScope scope, PostgresIndexSql sql,
        EngineQuery query, Match match, CancellationToken cancellationToken)
    {
        var plan = BuildPlan(scope, sql, query, match);
        var p = plan.Parameters;
        var facets = p.Add(query.Facets.Distinct(StringComparer.Ordinal).ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text);
        var scan = p.Add(query.FacetScanLimit, NpgsqlDbType.Integer);
        var max = p.Add(query.MaxFacetValues, NpgsqlDbType.Integer);

        var ctes = new[]
        {
            plan.With,
            $"matched AS (SELECT d.filters FROM {plan.From} WHERE {plan.Where} LIMIT {scan})",
            $"""
             counted AS (
                 SELECT f.name, v.value, count(*) AS n
                 FROM matched m
                 CROSS JOIN unnest({facets}::text[]) AS f(name)
                 CROSS JOIN LATERAL jsonb_array_elements_text(
                     CASE jsonb_typeof(m.filters -> f.name) WHEN 'array' THEN m.filters -> f.name
                     ELSE jsonb_build_array(m.filters -> f.name) END) AS v(value)
                 WHERE jsonb_exists(m.filters, f.name)
                 GROUP BY f.name, v.value)
             """
        }.Where(c => c.Length > 0);

        var values = new Dictionary<string, List<SearchFacetValue>>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(
                         $"WITH {string.Join(", ", ctes)} " +
                         "SELECT name, value, n FROM (SELECT name, value, n, row_number() OVER (PARTITION BY name ORDER BY n DESC, value) AS rn FROM counted) x " +
                         $"WHERE rn <= {max} ORDER BY name, n DESC, value",
                         connection, transaction))
        {
            p.CopyTo(command);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (!values.TryGetValue(name, out var list))
                    values[name] = list = [];
                list.Add(new SearchFacetValue(reader.GetString(1), (int)reader.GetInt64(2)));
            }
        }

        return query.Facets.Distinct(StringComparer.Ordinal)
            .Select(name => new SearchFacet(name, values.TryGetValue(name, out var list) ? list : []))
            .ToList();
    }

    // ----- plumbing --------------------------------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string> EmptyText = new Dictionary<string, string>();

    private NpgsqlConnection Connection => (NpgsqlConnection)db.Database.GetDbConnection();

    private NpgsqlTransaction? CurrentTransaction => db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

    private static PostgresIndexSql For(RegisteredIndex index) =>
        SqlByIndex.AddOrUpdate(index.Key, _ => new PostgresIndexSql(index),
            (_, cached) => ReferenceEquals(cached.Index, index) || cached.Index.Fingerprint == index.Fingerprint ? cached : new PostgresIndexSql(index));

    private static string With(string cte) => cte.Length == 0 ? "" : $"WITH {cte} ";

    private static IReadOnlyDictionary<string, string> ParseObject(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    private static void AddScope(NpgsqlCommand command, IndexScope scope)
    {
        command.Parameters.Add(new NpgsqlParameter("tenant", NpgsqlDbType.Uuid) { Value = scope.TenantId });
        command.Parameters.Add(new NpgsqlParameter("index", NpgsqlDbType.Text) { Value = scope.Index.Key });
    }

    private static void AddArray<T>(NpgsqlCommand command, string name, NpgsqlDbType type, T[] values) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Array | type) { Value = values });

    private Task<int> ExecuteAsync(string sql, IndexScope scope, Action<NpgsqlCommand> parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return RunAsync(searchSettings: false, async (connection, transaction) =>
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            AddScope(command, scope);
            parameters(command);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    private async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        if (Connection.State == ConnectionState.Open)
            return false;
        await db.Database.OpenConnectionAsync(cancellationToken); // runs the tenant session interceptor (RLS)
        return true;
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the context's connection. Searches get their own short transaction with a
    /// statement timeout and the trigram threshold (both <c>SET LOCAL</c>), unless the caller already has a transaction.
    /// Connection failures and timeouts become <see cref="SearchEngineUnavailableException"/>.
    /// </summary>
    private async Task<T> RunAsync<T>(
        bool searchSettings, Func<NpgsqlConnection, NpgsqlTransaction?, Task<T>> work, CancellationToken cancellationToken,
        double fuzzyThreshold = 0.4)
    {
        try
        {
            var opened = await OpenAsync(cancellationToken);
            try
            {
                if (!searchSettings || CurrentTransaction is not null)
                    return await work(Connection, CurrentTransaction);

                await using var transaction = await Connection.BeginTransactionAsync(cancellationToken);
                await using (var settings = new NpgsqlCommand(
                                 "SELECT set_config('statement_timeout', @timeout, true), " +
                                 "set_config('pg_trgm.word_similarity_threshold', @threshold, true)",
                                 Connection, transaction))
                {
                    settings.Parameters.Add(new NpgsqlParameter("timeout", NpgsqlDbType.Text)
                    {
                        Value = options.Value.Postgres.StatementTimeoutMs.ToString(CultureInfo.InvariantCulture)
                    });
                    settings.Parameters.Add(new NpgsqlParameter("threshold", NpgsqlDbType.Text)
                    {
                        Value = fuzzyThreshold.ToString(CultureInfo.InvariantCulture)
                    });
                    await settings.ExecuteNonQueryAsync(cancellationToken);
                }

                var result = await work(Connection, transaction);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            finally
            {
                if (opened)
                    await db.Database.CloseConnectionAsync();
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsUnavailable(ex))
        {
            throw new SearchEngineUnavailableException(Capabilities.Name, ex);
        }
    }

    /// <summary>The database is down, overloaded, or the statement timed out: not a bug in the query.</summary>
    private static bool IsUnavailable(Exception ex) => ex switch
    {
        PostgresException pg => pg.SqlState is "57014" or "57P01" or "57P02" or "57P03" or "53300" or "53400"
                                || pg.SqlState.StartsWith("08", StringComparison.Ordinal),
        NpgsqlException npgsql => npgsql.IsTransient,
        TimeoutException => true,
        _ => false
    };
}
