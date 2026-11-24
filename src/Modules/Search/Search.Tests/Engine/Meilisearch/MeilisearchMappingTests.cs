using System.Text.Json.Nodes;
using Search.Contracts.Indexing;
using Search.Contracts.Querying;
using Search.Engine;
using Search.Engine.Meilisearch;
using Search.Engine.Text;
using Search.Options;

namespace Search.Tests.Engine.Meilisearch;

internal static class Meili
{
    public static RegisteredIndex Index(SearchIndexDefinition? definition = null)
    {
        var d = definition ?? TestIndexes.Articles();
        return new SearchIndexRegistry([d]).Find(d.Key)!;
    }

    public static readonly Guid Tenant = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001");

    public static string Json(JsonNode? node) => node?.ToJsonString(MeilisearchClient.Json) ?? "null";
}

public sealed class MeilisearchNamesTests
{
    [Theory]
    [InlineData("", "catalog.products", "catalog--products")]
    [InlineData("prod_", "shop.order-lines", "prod_shop--order-_lines")]
    [InlineData("", "a.b_c.d", "a--b_c--d")]
    public void Index_uids_use_only_the_characters_meilisearch_allows(string prefix, string key, string uid) =>
        MeilisearchNames.IndexUid(prefix, key).Should().Be(uid).And.MatchRegex("^[A-Za-z0-9_-]+$");

    [Fact]
    public void Two_keys_never_share_an_index_uid()
    {
        string[] keys = ["a.b-c", "a.b.c", "a.b--c", "a.b_c", "a.b-_c", "a.b_-c", "ab.c", "a.bc"];

        keys.Select(k => MeilisearchNames.IndexUid("", k)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_document_key_joins_the_tenant_and_the_id() =>
        MeilisearchNames.DocumentKey(Meili.Tenant, "p-1").Should().Be("aaaaaaaa111100000000000000000001_p-1");
}

public sealed class MeilisearchFilterCompilerTests
{
    private static IndexScope Scope(IReadOnlyList<Guid>? branches = null) => new(Meili.Index(), Meili.Tenant, branches);

    private const string Tenant = "_tenant = \"aaaaaaaa111100000000000000000001\"";

    [Fact]
    public void The_tenant_is_always_the_first_condition()
    {
        var north = Guid.Parse("00000000-0000-0000-0000-00000000000a");

        MeilisearchFilterCompiler.Scope(Scope()).Should().Be(Tenant);
        MeilisearchFilterCompiler.Scope(Scope([])).Should().Be($"{Tenant} AND _branch IS NULL");
        MeilisearchFilterCompiler.Scope(Scope([north]))
            .Should().Be($"{Tenant} AND (_branch IS NULL OR _branch IN [\"{north}\"])");
    }

    [Fact]
    public void Every_filter_type_compiles_on_its_filter_attribute()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SearchFilter[] filters =
        [
            new KeywordFilter("status", "Published"),
            new KeywordsFilter("tag_ids", ["a", "b"]),
            new KeywordsFilter("tag_ids", ["a", "b"], SearchMatch.All),
            new NumberRangeFilter("price", 1.5m, null),
            new NumberRangeFilter("price", 1, 9),
            new DateRangeFilter("published_at", from, null),
            new BoolFilter("featured", false)
        ];

        MeilisearchFilterCompiler.Compile(Scope(), filters).Should().Be(
            $"{Tenant} AND f_status = \"Published\" AND f_tag_ids IN [\"a\", \"b\"] AND (f_tag_ids = \"a\" AND f_tag_ids = \"b\") " +
            "AND f_price >= 1.5 AND (f_price >= 1 AND f_price <= 9) AND f_published_at >= 1767225600000 AND f_featured = false");
    }

    [Fact]
    public void A_value_can_never_break_out_of_its_quotes()
    {
        var compiled = MeilisearchFilterCompiler.Compile(Scope(), [new KeywordFilter("status", "x\" OR _tenant = \"other\\")]);

        compiled.Should().EndWith("f_status = \"x\\\" OR _tenant = \\\"other\\\\\"");
    }

    [Fact]
    public void The_sweep_selects_older_stamps_written_before_the_rebuild_started() =>
        MeilisearchFilterCompiler.Sweep(Scope(), 7, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().Be($"{Tenant} AND _stamp < 7 AND _indexed_at < 1767225600000");
}

public sealed class MeilisearchQueryTextTests
{
    private static IReadOnlyList<string> Build(string text, SearchMode mode = SearchMode.Full, int? fuzzy = null) =>
        MeilisearchQueryText.Build(SearchQueryParser.Parse(text, mode), fuzzy);

    [Fact]
    public void Words_and_phrases_are_quoted_so_they_match_exactly() =>
        Build("gaming \"red lamp\"").Should().Equal("\"gaming\" \"red lamp\"");

    [Fact]
    public void Exclusions_become_negated_quoted_terms() =>
        Build("laptop -used").Should().Equal("\"laptop\" -\"used\"");

    [Fact]
    public void The_autocomplete_prefix_stays_unquoted_and_last() =>
        Build("lap gaming", SearchMode.Autocomplete).Should().Equal("\"lap\" gaming");

    [Fact]
    public void The_fuzzy_pass_loosens_only_words_long_enough() =>
        Build("laptpo tv", fuzzy: 3).Should().Equal("\"tv\" laptpo");

    [Fact]
    public void Alternatives_expand_into_one_query_per_combination() =>
        Build("red or blue lamp").Should().Equal("\"red\" \"lamp\"", "\"blue\" \"lamp\"");

    [Fact]
    public void Expansion_is_capped()
    {
        var text = string.Join(' ', Enumerable.Range(0, 5).Select(i => $"a{i} or b{i}"));

        Build(text).Should().HaveCount(MeilisearchQueryText.MaxCombinations);
    }

    [Fact]
    public void No_text_browses() => MeilisearchQueryText.Build(ParsedSearchQuery.Empty).Should().Equal("");

    [Theory]
    [InlineData("laptpo", 3, true)]
    [InlineData("tv", 3, false)]
    [InlineData("\"red lamp\"", 3, false)]
    [InlineData("laptpo", null, false)]
    public void A_fuzzy_pass_runs_only_when_a_word_is_long_enough(string text, int? min, bool allowed) =>
        MeilisearchQueryText.AllowsFuzzy(SearchQueryParser.Parse(text), min).Should().Be(allowed);
}

public sealed class MeilisearchIndexSettingsTests
{
    [Fact]
    public void Settings_follow_the_definition()
    {
        var options = new SearchOptions();
        options.Facets.MaxValues = 30;

        var settings = MeilisearchIndexSettings.Build(Meili.Index(), options);

        Meili.Json(settings["searchableAttributes"]).Should().Be("[\"t_title\",\"t_body\"]");
        Meili.Json(settings["rankingRules"]).Should().StartWith("[\"sort\",\"words\"");
        Meili.Json(settings["filterableAttributes"]).Should().Be(
            "[\"_tenant\",\"_branch\",\"_stamp\",\"_indexed_at\",\"_id\",\"f_status\",\"f_tag_ids\",\"f_price\",\"f_published_at\",\"f_featured\"]");
        Meili.Json(settings["sortableAttributes"]).Should().Be("[\"_id\",\"f_price\",\"f_published_at\"]");
        Meili.Json(settings["localizedAttributes"]).Should().Be(
            "[{\"attributePatterns\":[\"t_title\"],\"locales\":[\"ara\",\"eng\"]},{\"attributePatterns\":[\"t_body\"],\"locales\":[\"eng\"]}]");
        settings["faceting"]!["maxValuesPerFacet"]!.GetValue<int>().Should().Be(30);
        Meili.Json(settings["displayedAttributes"]).Should().Be("[\"_id\",\"_version\",\"_highlight\"]");
    }

    [Fact]
    public void Heavier_fields_are_searched_first_whatever_their_order_in_the_definition()
    {
        var definition = TestIndexes.Articles() with
        {
            TextFields =
            [
                new SearchTextField("body", SearchWeight.Low, SearchAnalyzer.Simple),
                new SearchTextField("code", SearchWeight.Highest, SearchAnalyzer.Simple),
                new SearchTextField("title", SearchWeight.Highest, SearchAnalyzer.Simple)
            ]
        };

        Meili.Json(MeilisearchIndexSettings.Build(Meili.Index(definition), new SearchOptions())["searchableAttributes"])
            .Should().Be("[\"t_code\",\"t_title\",\"t_body\"]");
    }
}

public sealed class MeilisearchDocumentsTests
{
    private static EngineDocument Doc(string id, long version, Guid? branch = null) =>
        SearchDocumentPreparer.Prepare(Meili.Index(), SearchDocument.For(id, version).InBranch(branch)
            .Text("title", "Café Lamp").Text("body", "Bright")
            .Keyword("status", "Published").Keywords("tag_ids", ["a", "b"]).Number("price", 9.5m)
            .Date("published_at", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Bool("featured", true)
            .Build());

    [Fact]
    public void Within_a_batch_the_newest_version_of_an_id_wins() =>
        MeilisearchDocuments.LatestPerId([Doc("a", 1), Doc("b", 1), Doc("a", 3), Doc("a", 2)])
            .Select(d => (d.Id, d.Version)).Should().BeEquivalentTo([("a", 3L), ("b", 1L)]);

    [Fact]
    public void A_document_carries_its_tenant_branch_version_and_stamp_beside_its_fields()
    {
        var json = MeilisearchDocuments.ToJson(Meili.Tenant, Doc("p1", 4), 7, new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc));

        Meili.Json(json).Should().Be(
            "{\"_key\":\"aaaaaaaa111100000000000000000001_p1\",\"_id\":\"p1\",\"_tenant\":\"aaaaaaaa111100000000000000000001\"," +
            "\"_branch\":null,\"_version\":4,\"_stamp\":7,\"_indexed_at\":1767225601000," +
            "\"t_title\":\"cafe lamp\",\"t_body\":\"bright\"," +
            "\"f_status\":\"Published\",\"f_tag_ids\":[\"a\",\"b\"],\"f_price\":9.5,\"f_published_at\":1767225600000,\"f_featured\":true," +
            "\"_highlight\":{\"title\":\"Café Lamp\"}}");
    }
}

public sealed class MeilisearchSearchesTests
{
    private static EngineQuery Query(string? text, Func<EngineQuery, EngineQuery>? change = null)
    {
        var query = new EngineQuery { Text = SearchQueryParser.Parse(text), Limit = 10 };
        return change?.Invoke(query) ?? query;
    }

    [Fact]
    public void Aligned_pages_ask_for_an_exact_total()
    {
        var body = MeilisearchSearches.Single("\"lamp\"", "f", Query("lamp", q => q with { Offset = 20, Facets = ["status"] }), Meili.Index());

        body["page"]!.GetValue<int>().Should().Be(3);
        body["hitsPerPage"]!.GetValue<int>().Should().Be(10);
        Meili.Json(body["facets"]).Should().Be("[\"f_status\"]");
        Meili.Json(body["attributesToRetrieve"]).Should().Be("[\"_id\"]");
        body["matchingStrategy"]!.GetValue<string>().Should().Be("all");
        body.ContainsKey("sort").Should().BeFalse("text without a sort ranks by relevance");
    }

    [Fact]
    public void Unaligned_offsets_use_offset_and_limit()
    {
        var body = MeilisearchSearches.Single("", "f", Query(null, q => q with { Offset = 5 }), Meili.Index());

        (body["offset"]!.GetValue<int>(), body["limit"]!.GetValue<int>()).Should().Be((5, 10));
    }

    [Fact]
    public void Sorting_always_ends_with_the_id_and_browsing_uses_the_default_sort()
    {
        MeilisearchSearches.Sort(Query("lamp", q => q with { Sort = new SearchSort("price") }), Meili.Index())
            .Should().Equal("f_price:asc", "_id:asc");
        MeilisearchSearches.Sort(Query(null), Meili.Index()).Should().Equal("f_published_at:desc", "_id:asc");
        MeilisearchSearches.Sort(Query("lamp"), Meili.Index()).Should().BeEmpty();
    }

    [Fact]
    public void A_federated_search_shares_the_filter_and_merges_facets()
    {
        var body = MeilisearchSearches.Federated("uid", ["\"a\"", "\"b\""], "f", Query("a or b", q => q with { Facets = ["status"] }), Meili.Index());

        body["queries"]!.AsArray().Should().HaveCount(2);
        body["queries"]![1]!["indexUid"]!.GetValue<string>().Should().Be("uid");
        body["queries"]![1]!["filter"]!.GetValue<string>().Should().Be("f");
        Meili.Json(body["federation"]!["facetsByIndex"]).Should().Be("{\"uid\":[\"f_status\"]}");
    }

    [Fact]
    public void A_response_becomes_hits_total_and_ordered_trimmed_facets()
    {
        var response = JsonNode.Parse("""
            {"hits":[{"_id":"a","_rankingScore":0.9,"_highlight":{"title":"A"}},{"_id":"b"}],"totalHits":12,
             "facetDistribution":{"f_tag_ids":{"z":1,"x":4,"y":4,"w":2}}}
            """)!;

        var result = MeilisearchSearches.Read(response, Query("lamp", q => q with
        {
            Facets = ["tag_ids", "status"],
            MaxFacetValues = 3,
            IncludeHighlightText = true
        }), SearchMatchMode.Exact, exhaustiveFacets: true);

        result.Hits.Select(h => (h.Id, h.Score)).Should().Equal(("a", 0.9), ("b", 0));
        result.Hits[0].HighlightText.Should().Equal(new Dictionary<string, string> { ["title"] = "A" });
        result.Total.Should().Be(12);
        result.Facets.Select(f => f.Field).Should().Equal("tag_ids", "status");
        result.Facets[0].Values.Should().Equal(new SearchFacetValue("x", 4), new SearchFacetValue("y", 4), new SearchFacetValue("w", 2));
        result.Facets[1].Values.Should().BeEmpty();
    }

    [Fact]
    public void Highlight_text_is_dropped_unless_requested() =>
        MeilisearchSearches.Read(JsonNode.Parse("""{"hits":[{"_id":"a","_highlight":{"title":"A"}}],"estimatedTotalHits":1}""")!,
                Query("a"), SearchMatchMode.Exact, true)
            .Hits.Single().HighlightText.Should().BeEmpty();
}
