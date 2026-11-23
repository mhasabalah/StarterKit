using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Search.Engine.Text;

namespace Search.Engine.Postgres;

/// <summary>Collects the parameters of one command; values are never written into SQL text.</summary>
internal sealed class SqlParameters
{
    private readonly List<NpgsqlParameter> _parameters = [];

    /// <summary>Adds a parameter and returns its placeholder (<c>@p3</c>).</summary>
    public string Add(object? value, NpgsqlDbType? type = null)
    {
        var name = "p" + _parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var parameter = type is { } t ? new NpgsqlParameter(name, t) : new NpgsqlParameter { ParameterName = name };
        parameter.Value = value ?? DBNull.Value;
        _parameters.Add(parameter);
        return "@" + name;
    }

    public void CopyTo(NpgsqlCommand command) => command.Parameters.AddRange(_parameters.ToArray());
}

/// <summary>
/// The PostgreSQL text search side of an index: which configurations analyze each field, the weight letters, and the
/// upsert statement (built once per index). Configuration names and weight letters come from closed enums only.
/// </summary>
internal sealed class PostgresIndexSql
{
    public const string Table = "search.search_documents";

    /// <summary><c>ts_rank_cd</c> weights in D, C, B, A order (Low .. Highest).</summary>
    public const string RankWeights = "'{0.1, 0.2, 0.4, 1.0}'";

    /// <summary>
    /// Put between the vectors of two fields, and between the two analyses of a bilingual field: concatenation would
    /// otherwise make the last word of one and the first word of the next adjacent, and a phrase could match across
    /// them. The lexeme <c>_</c> is never produced by a query (query words are letters and digits only).
    /// </summary>
    public const string PositionGap = "'_:50'::tsvector";

    public PostgresIndexSql(RegisteredIndex index)
    {
        Index = index;
        TextFields = index.Definition.TextFields;
        QueryConfigurations = TextFields.SelectMany(f => Configurations(f.Analyzer)).Distinct().ToList();
        UpsertSql = BuildUpsert();
    }

    public RegisteredIndex Index { get; }

    public IReadOnlyList<SearchTextField> TextFields { get; }

    /// <summary>Every configuration a document vector of this index was built with; queries match through all of them.</summary>
    public IReadOnlyList<string> QueryConfigurations { get; }

    public string UpsertSql { get; }

    public static IReadOnlyList<string> Configurations(SearchAnalyzer analyzer) => analyzer switch
    {
        SearchAnalyzer.Simple => ["simple"],
        SearchAnalyzer.English => ["english"],
        SearchAnalyzer.Arabic => ["arabic"],
        SearchAnalyzer.Bilingual => ["english", "arabic"],
        _ => throw new ArgumentOutOfRangeException(nameof(analyzer), analyzer, "Unknown analyzer.")
    };

    public static char WeightLetter(SearchWeight weight) => weight switch
    {
        SearchWeight.Low => 'D',
        SearchWeight.Medium => 'C',
        SearchWeight.High => 'B',
        SearchWeight.Highest => 'A',
        _ => throw new ArgumentOutOfRangeException(nameof(weight), weight, "Unknown weight.")
    };

    private string BuildUpsert()
    {
        var vector = string.Join($" || {PositionGap} || ", TextFields.SelectMany((field, i) =>
            Configurations(field.Analyzer).Select(c =>
                $"setweight(to_tsvector('{c}', coalesce(v.t{i}, '')), '{WeightLetter(field.Weight)}')")));

        var textArrays = string.Concat(TextFields.Select((_, i) => $", @t{i}::text[]"));
        var textColumns = string.Concat(TextFields.Select((_, i) => $", t{i}"));

        return $"""
            INSERT INTO {Table} AS d (tenant_id, index_key, document_id, branch_id, source_version, rebuild_stamp,
                                      search_vector, suggest_text, filters, content, indexed_at)
            SELECT @tenant, @index, v.id, v.branch_id, v.version, @stamp,
                   {vector},
                   v.suggest, v.filters::jsonb, v.content::jsonb, @now
            FROM unnest(@ids::text[], @branches::uuid[], @versions::bigint[], @suggest::text[], @filters::text[],
                        @content::text[]{textArrays})
                 AS v(id, branch_id, version, suggest, filters, content{textColumns})
            ON CONFLICT (tenant_id, index_key, document_id) DO UPDATE SET
                branch_id = EXCLUDED.branch_id, source_version = EXCLUDED.source_version,
                rebuild_stamp = EXCLUDED.rebuild_stamp, search_vector = EXCLUDED.search_vector,
                suggest_text = EXCLUDED.suggest_text, filters = EXCLUDED.filters, content = EXCLUDED.content,
                indexed_at = EXCLUDED.indexed_at
            WHERE d.source_version <= EXCLUDED.source_version
            """;
    }

    /// <summary>The filter values as stored: keyword, list of keywords, number, date (epoch milliseconds) or boolean.</summary>
    public static string FiltersJson(IReadOnlyDictionary<string, SearchValue> filters)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            foreach (var (name, value) in filters.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                json.WritePropertyName(name);
                switch (value)
                {
                    case KeywordValue k:
                        json.WriteStringValue(k.Value);
                        break;
                    case KeywordListValue list:
                        json.WriteStartArray();
                        foreach (var item in list.Values)
                            json.WriteStringValue(item);
                        json.WriteEndArray();
                        break;
                    case NumberValue n:
                        json.WriteNumberValue(n.Value);
                        break;
                    case DateValue d:
                        json.WriteNumberValue(EpochMilliseconds(d.Value));
                        break;
                    case BooleanValue b:
                        json.WriteBooleanValue(b.Value);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(filters), value, "Unknown search value.");
                }
            }

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string ObjectJson(IReadOnlyDictionary<string, string> values) =>
        JsonSerializer.Serialize(values.OrderBy(v => v.Key, StringComparer.Ordinal).ToDictionary(v => v.Key, v => v.Value));

    public static long EpochMilliseconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
}

/// <summary>
/// Compiles a <see cref="ParsedSearchQuery"/> into a <c>tsquery</c> expression over the index's configurations. A word
/// that some configuration drops as a stop word ("the" in English) is dropped as a whole, so it never becomes a
/// requirement through another configuration.
/// </summary>
internal static class PostgresTsQueryCompiler
{
    public static string Compile(ParsedSearchQuery query, IReadOnlyList<string> configurations, SqlParameters parameters)
    {
        ArgumentOutOfRangeException.ThrowIfZero(configurations.Count);
        var clauses = query.Clauses.Select(clause =>
            "(" + string.Join(" || ", clause.Alternatives.Select(term => Term(term, configurations, parameters))) + ")");
        return string.Join(" && ", clauses);
    }

    private static string Term(SearchTerm term, IReadOnlyList<string> configurations, SqlParameters parameters)
    {
        string Function(string configuration, string placeholder) =>
            term.IsPhrase ? $"phraseto_tsquery('{configuration}', {placeholder})"
            : term.Prefix ? $"to_tsquery('{configuration}', {placeholder} || ':*')"
            : $"plainto_tsquery('{configuration}', {placeholder})";

        // Words are letters and digits only (SearchQueryParser), so the prefix form cannot inject tsquery syntax.
        var value = parameters.Add(string.Join(' ', term.Words), NpgsqlDbType.Text);
        var each = configurations.Select(c => Function(c, value)).ToList();
        var anyEmpty = string.Join(" OR ", each.Select(f => $"numnode({f}) = 0"));
        var expression = $"(CASE WHEN {anyEmpty} THEN ''::tsquery ELSE ({string.Join(" || ", each)}) END)";
        return term.Negated ? $"!!{expression}" : expression;
    }
}

/// <summary>Compiles validated filters into predicates on the <c>filters</c> jsonb column (field names are parameters).</summary>
internal static class PostgresFilterCompiler
{
    public static IEnumerable<string> Compile(RegisteredIndex index, IReadOnlyList<SearchFilter> filters, SqlParameters parameters)
    {
        foreach (var filter in filters)
        {
            var field = index.FilterFields[filter.Field];
            var list = field.Type == SearchFieldType.KeywordList;

            yield return filter switch
            {
                KeywordFilter k => Contains(parameters, field.Name, list ? new[] { k.Value } : (object)k.Value),
                KeywordsFilter { Match: SearchMatch.All } k => Contains(parameters, field.Name, k.Values),
                KeywordsFilter k when list =>
                    $"jsonb_exists_any(d.filters -> {Name(parameters, field)}, {parameters.Add(k.Values.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text)})",
                KeywordsFilter k =>
                    $"(d.filters ->> {Name(parameters, field)}) = ANY({parameters.Add(k.Values.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text)})",
                NumberRangeFilter n => Range(parameters, field, n.Min, n.Max),
                DateRangeFilter d => Range(parameters, field,
                    d.From is { } from ? PostgresIndexSql.EpochMilliseconds(from) : null,
                    d.To is { } to ? PostgresIndexSql.EpochMilliseconds(to) : null),
                BoolFilter b => Contains(parameters, field.Name, b.Value),
                _ => throw new ArgumentOutOfRangeException(nameof(filters), filter, "Unknown search filter.")
            };
        }
    }

    /// <summary>The ORDER BY expression of a sortable filter field.</summary>
    public static string SortExpression(SearchFilterField field, bool descending, SqlParameters parameters)
    {
        var direction = descending ? "DESC" : "ASC";
        var value = field.Type is SearchFieldType.Number or SearchFieldType.Date
            ? $"(d.filters ->> {Name(parameters, field)})::numeric"
            : $"(d.filters ->> {Name(parameters, field)}) COLLATE \"C\"";
        return $"{value} {direction} NULLS LAST";
    }

    private static string Name(SqlParameters parameters, SearchFilterField field) => parameters.Add(field.Name, NpgsqlDbType.Text);

    private static string Contains(SqlParameters parameters, string field, object value) =>
        $"d.filters @> {parameters.Add(JsonSerializer.Serialize(new Dictionary<string, object> { [field] = value }), NpgsqlDbType.Jsonb)}";

    private static string Range(SqlParameters parameters, SearchFilterField field, decimal? min, decimal? max)
    {
        // A document without the value has NULL here, which no bound matches.
        var value = $"(d.filters ->> {Name(parameters, field)})::numeric";
        var parts = new List<string>();
        if (min is { } lower)
            parts.Add($"{value} >= {parameters.Add(lower, NpgsqlDbType.Numeric)}");
        if (max is { } upper)
            parts.Add($"{value} <= {parameters.Add(upper, NpgsqlDbType.Numeric)}");
        return parts.Count == 0 ? $"{value} IS NOT NULL" : "(" + string.Join(" AND ", parts) + ")";
    }
}
