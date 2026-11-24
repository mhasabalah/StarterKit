using Npgsql;
using Search.Contracts.Indexing;
using Search.Contracts.Querying;
using Search.Engine;
using Search.Engine.Postgres;
using Search.Engine.Text;

namespace Search.Tests.Engine;

public sealed class PostgresSqlTests
{
    private static readonly RegisteredIndex Index = new SearchIndexRegistry([TestIndexes.Articles()]).Find("test.articles")!;

    private static (string Sql, NpgsqlParameterCollection Parameters) Command(Func<SqlParameters, string> build)
    {
        var parameters = new SqlParameters();
        var sql = build(parameters);
        var command = new NpgsqlCommand(sql);
        parameters.CopyTo(command);
        return (sql, command.Parameters);
    }

    [Fact]
    public void Each_analyzer_maps_to_its_configurations()
    {
        PostgresIndexSql.Configurations(SearchAnalyzer.Simple).Should().Equal("simple");
        PostgresIndexSql.Configurations(SearchAnalyzer.English).Should().Equal("english");
        PostgresIndexSql.Configurations(SearchAnalyzer.Arabic).Should().Equal("arabic");
        PostgresIndexSql.Configurations(SearchAnalyzer.Bilingual).Should().Equal("english", "arabic");
        new PostgresIndexSql(Index).QueryConfigurations.Should().Equal("english", "arabic");
    }

    [Theory]
    [InlineData(SearchWeight.Low, 'D')]
    [InlineData(SearchWeight.Medium, 'C')]
    [InlineData(SearchWeight.High, 'B')]
    [InlineData(SearchWeight.Highest, 'A')]
    public void Weights_map_to_tsvector_letters(SearchWeight weight, char letter) =>
        PostgresIndexSql.WeightLetter(weight).Should().Be(letter);

    [Fact]
    public void The_upsert_weights_every_analysis_separates_them_by_a_gap_and_guards_versions()
    {
        var sql = new PostgresIndexSql(Index).UpsertSql;

        sql.Should().Contain("setweight(to_tsvector('english', coalesce(v.t0, '')), 'A')")
            .And.Contain("setweight(to_tsvector('arabic', coalesce(v.t0, '')), 'A')")
            .And.Contain("setweight(to_tsvector('english', coalesce(v.t1, '')), 'D')")
            .And.Contain(PostgresIndexSql.PositionGap)
            .And.Contain("WHERE d.source_version <= EXCLUDED.source_version")
            .And.Contain("@tenant").And.Contain("@index");
        sql.Should().NotContain("title").And.NotContain("body", "field names never reach the SQL text");
    }

    [Fact]
    public void Filter_values_are_stored_as_typed_json()
    {
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var json = PostgresIndexSql.FiltersJson(new Dictionary<string, SearchValue>
        {
            ["status"] = new KeywordValue("Pub\"lished"),
            ["tag_ids"] = new KeywordListValue(["a", "b"]),
            ["price"] = new NumberValue(10.5m),
            ["published_at"] = new DateValue(at),
            ["featured"] = new BooleanValue(true)
        });

        json.Should().Be("{\"featured\":true,\"price\":10.5,\"published_at\":1767225600000,\"status\":\"Pub\\u0022lished\",\"tag_ids\":[\"a\",\"b\"]}");
    }

    [Fact]
    public void A_query_compiles_words_into_parameters_never_into_sql_text()
    {
        var query = SearchQueryParser.Parse("evil'); DROP TABLE x;-- \"red lamp\" -used or other lap", SearchMode.Autocomplete);

        var (sql, parameters) = Command(p => PostgresTsQueryCompiler.Compile(query, ["english", "arabic"], p));

        sql.Should().NotContain("evil").And.NotContain("DROP").And.NotContain("lamp");
        sql.Should().Contain("plainto_tsquery('english', @p0)")
            .And.Contain("phraseto_tsquery('arabic', @p")
            .And.Contain("to_tsquery('english', @p").And.Contain(" || ':*')")
            .And.Contain("!!")
            .And.Contain("numnode(");
        parameters.Select(p => p.Value).Should().Contain("red lamp").And.Contain("lap");
    }

    [Fact]
    public void Filters_compile_to_jsonb_predicates_with_parameterized_names_and_values()
    {
        var filters = new SearchFilter[]
        {
            new KeywordFilter("status", "x' OR 1=1"),
            new KeywordFilter("tag_ids", "t"),
            new KeywordsFilter("status", ["a", "b"]),
            new KeywordsFilter("tag_ids", ["a", "b"]),
            new KeywordsFilter("tag_ids", ["a", "b"], SearchMatch.All),
            new NumberRangeFilter("price", 1, 2),
            new NumberRangeFilter("price", null, null),
            new DateRangeFilter("published_at", DateTime.UtcNow, null),
            new BoolFilter("featured", true)
        };

        var (sql, parameters) = Command(p => string.Join(" AND ", PostgresFilterCompiler.Compile(Index, filters, p)));

        sql.Should().NotContain("status").And.NotContain("tag_ids").And.NotContain("OR 1=1");
        sql.Should().Contain("d.filters @>").And.Contain("jsonb_exists_any(").And.Contain("= ANY(").And.Contain("::numeric >=")
            .And.Contain("IS NOT NULL");
        parameters.Select(p => p.Value?.ToString()).Should()
            .Contain("{\"status\":\"x\\u0027 OR 1=1\"}")
            .And.Contain("{\"tag_ids\":[\"t\"]}")
            .And.Contain("{\"tag_ids\":[\"a\",\"b\"]}")
            .And.Contain("{\"featured\":true}");
    }

    [Theory]
    [InlineData("price", false, "::numeric ASC NULLS LAST")]
    [InlineData("published_at", true, "::numeric DESC NULLS LAST")]
    [InlineData("status", false, "COLLATE \"C\" ASC NULLS LAST")]
    public void Sort_expressions_cast_by_field_type(string field, bool descending, string expected)
    {
        var (sql, _) = Command(p => PostgresFilterCompiler.SortExpression(Index.FilterFields[field], descending, p));

        sql.Should().EndWith(expected).And.NotContain(field);
    }
}
