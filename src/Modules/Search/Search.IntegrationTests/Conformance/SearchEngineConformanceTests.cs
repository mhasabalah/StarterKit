using Search.Contracts.Indexing;
using Search.Contracts.Querying;
using Search.Engine;
using Search.Engine.Text;
using static Search.IntegrationTests.TestIndex.TestArticles;

namespace Search.IntegrationTests.Conformance;

/// <summary>What the conformance suite needs from an engine's test fixture.</summary>
public interface ISearchEngineTestHost
{
    RegisteredIndex Index { get; }

    Task<T> RunAsync<T>(Guid tenantId, Func<ISearchEngine, Task<T>> action, IReadOnlyList<Guid>? branches = null);
}

/// <summary>
/// The behavior every <see cref="ISearchEngine"/> must have (ADR-0036). An adapter passes the suite by subclassing it
/// with its fixture; a difference an engine cannot provide is a <see cref="SearchEngineCapabilities"/> flag, never a
/// skipped expectation. Every test works in fresh tenants, so tests never see each other's documents.
/// </summary>
public abstract class SearchEngineConformanceTests(ISearchEngineTestHost host)
{
    // ----- helpers ---------------------------------------------------------------------------------------------------

    protected static Guid NewTenant() => Guid.NewGuid();

    protected Task IndexAsync(Guid tenant, params SearchDocument[] documents) => IndexWithStampAsync(tenant, 1, documents);

    protected Task IndexWithStampAsync(Guid tenant, long stamp, params SearchDocument[] documents) =>
        host.RunAsync(tenant, async engine =>
        {
            await engine.UpsertAsync(Scope(tenant), documents.Select(d => SearchDocumentPreparer.Prepare(host.Index, d)).ToList(),
                stamp, CancellationToken.None);
            return true;
        });

    protected Task<EngineSearchResult> SearchAsync(
        Guid tenant, string? text, Func<EngineQuery, EngineQuery>? change = null, IReadOnlyList<Guid>? branches = null,
        SearchMode mode = SearchMode.Full)
    {
        var query = new EngineQuery
        {
            Text = SearchQueryParser.Parse(text, mode),
            FuzzyMinTermLength = 3,
            Limit = 50
        };
        query = change?.Invoke(query) ?? query;
        return host.RunAsync(tenant, engine => engine.SearchAsync(Scope(tenant, branches), query, CancellationToken.None), branches);
    }

    protected async Task<string[]> IdsAsync(Guid tenant, string? text, Func<EngineQuery, EngineQuery>? change = null,
        IReadOnlyList<Guid>? branches = null, SearchMode mode = SearchMode.Full) =>
        [.. (await SearchAsync(tenant, text, change, branches, mode)).Hits.Select(h => h.Id)];

    protected IndexScope Scope(Guid tenant, IReadOnlyList<Guid>? branches = null) => new(host.Index, tenant, branches);

    protected SearchEngineCapabilities Capabilities() =>
        host.RunAsync(Guid.NewGuid(), engine => Task.FromResult(engine.Capabilities)).GetAwaiter().GetResult();

    // ----- writing ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Upsert_ThenSearch_FindsTheDocument()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop"), Article("a2", "Desk lamp"));

        (await IdsAsync(tenant, "laptop")).Should().Equal("a1");
    }

    [Fact]
    public async Task Upsert_Twice_ReplacesTheDocument()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop"));
        await IndexAsync(tenant, Article("a1", "Office chair", version: 2));

        (await IdsAsync(tenant, "laptop")).Should().BeEmpty();
        (await IdsAsync(tenant, "chair")).Should().Equal("a1");
        (await host.RunAsync(tenant, e => e.CountAsync(Scope(tenant), CancellationToken.None))).Should().Be(1);
    }

    [Fact]
    public async Task Upsert_OlderVersionAfterNewer_IsIgnored_WhenTheEngineGuardsVersions()
    {
        if (!Capabilities().VersionGuard)
            return;

        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Office chair", version: 5));
        await IndexAsync(tenant, Article("a1", "Gaming laptop", version: 4));

        (await IdsAsync(tenant, "chair")).Should().Equal("a1");
        (await IdsAsync(tenant, "laptop")).Should().BeEmpty();
    }

    [Fact]
    public async Task Upsert_TheSameIdTwiceInOneBatch_KeepsTheNewestVersion()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop", version: 1), Article("a1", "Office chair", version: 2));

        (await IdsAsync(tenant, "chair")).Should().Equal("a1");
    }

    [Fact]
    public async Task Delete_RemovesTheDocuments_AndIgnoresUnknownIds()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop"), Article("a2", "Laptop bag"));

        await host.RunAsync(tenant, async e =>
        {
            await e.DeleteAsync(Scope(tenant), ["a1", "missing"], CancellationToken.None);
            return true;
        });

        (await IdsAsync(tenant, "laptop")).Should().Equal("a2");
    }

    [Fact]
    public async Task Sweep_RemovesDocumentsTheRebuildDidNotReach_AndKeepsLaterWrites()
    {
        var tenant = NewTenant();
        await IndexWithStampAsync(tenant, 1, Article("old", "Laptop old"), Article("kept", "Laptop kept"));
        await Task.Delay(20);
        var rebuildStartedAt = DateTime.UtcNow;
        await Task.Delay(20);
        await IndexWithStampAsync(tenant, 2, Article("kept", "Laptop kept"));
        await IndexWithStampAsync(tenant, 1, Article("live", "Laptop live")); // a live write during the rebuild

        var swept = await host.RunAsync(tenant, e => e.SweepAsync(Scope(tenant), 2, rebuildStartedAt, CancellationToken.None));

        swept.Should().Be(1);
        (await IdsAsync(tenant, "laptop")).Should().BeEquivalentTo(["kept", "live"]);
    }

    [Fact]
    public async Task StreamStamps_AndCount_CoverEveryDocumentOfTheTenant_InEveryBranch()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "One", version: 3), Article("a2", "Two", branch: Guid.NewGuid(), version: 7));
        await IndexAsync(NewTenant(), Article("other", "Other"));

        var stamps = await host.RunAsync(tenant, async e =>
        {
            var list = new List<SearchDocumentStamp>();
            await foreach (var stamp in e.StreamStampsAsync(Scope(tenant, [Guid.NewGuid()]), CancellationToken.None))
                list.Add(stamp);
            return list;
        });

        stamps.Should().BeEquivalentTo([new SearchDocumentStamp("a1", 3), new SearchDocumentStamp("a2", 7)]);
        (await host.RunAsync(tenant, e => e.CountAsync(Scope(tenant), CancellationToken.None))).Should().Be(2);
    }

    [Fact]
    public async Task DeleteTenant_RemovesOnlyThatTenant()
    {
        var (a, b) = (NewTenant(), NewTenant());
        await IndexAsync(a, Article("a1", "Laptop"));
        await IndexAsync(b, Article("b1", "Laptop"));

        await host.RunAsync(a, async e =>
        {
            await e.DeleteTenantAsync(a, CancellationToken.None);
            return true;
        });

        (await IdsAsync(a, "laptop")).Should().BeEmpty();
        (await IdsAsync(b, "laptop")).Should().Equal("b1");
    }

    // ----- isolation -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Search_NeverReturnsAnotherTenantsDocuments_NorCountsThem()
    {
        var (a, b) = (NewTenant(), NewTenant());
        await IndexAsync(a, Article("a1", "Laptop", tags: ["x"]));
        await IndexAsync(b, Article("b1", "Laptop", tags: ["x"]), Article("b2", "Laptop", tags: ["y"]));

        var result = await SearchAsync(a, "laptop", q => q with { Facets = ["tag_ids"] });

        result.Hits.Select(h => h.Id).Should().Equal("a1");
        result.Total.Should().Be(1);
        result.Facets.Single().Values.Should().Equal(new SearchFacetValue("x", 1));
        (await IdsAsync(a, null)).Should().Equal("a1");
    }

    [Fact]
    public async Task A_RestrictedBranchScope_SeesItsBranchesAndTenantWideDocumentsOnly()
    {
        var tenant = NewTenant();
        var (north, south) = (Guid.NewGuid(), Guid.NewGuid());
        await IndexAsync(tenant,
            Article("wide", "Laptop"), Article("north", "Laptop", branch: north), Article("south", "Laptop", branch: south));

        (await IdsAsync(tenant, "laptop", branches: [north])).Should().BeEquivalentTo(["wide", "north"]);
        (await IdsAsync(tenant, "laptop", branches: [])).Should().Equal("wide");
        (await IdsAsync(tenant, "laptop")).Should().BeEquivalentTo(["wide", "north", "south"]);
    }

    // ----- matching --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("مُحَمَّد أحمد", "محمد احمد")]
    [InlineData("مدرسة الإسلام", "مدرسه")]
    [InlineData("مستشفى", "مستشفي")]
    [InlineData("أداة", "اداه")]
    [InlineData("طراز ٢٠٢٦", "2026")]
    [InlineData("مسؤول المبيعات", "مسوول")]
    public async Task Arabic_MatchesRegardlessOfDiacriticsHamzaTaMarbutaAlefMaqsuraAndDigits(string indexed, string searched)
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("ar", indexed), Article("other", "شيء آخر"));

        (await IdsAsync(tenant, searched)).Should().Equal("ar");
    }

    [Fact]
    public async Task Latin_MatchesRegardlessOfCaseAndAccents()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Café CRÈME"));

        (await IdsAsync(tenant, "cafe creme")).Should().Equal("a1");
        (await IdsAsync(tenant, "CAFÉ")).Should().Equal("a1");
    }

    [Fact]
    public async Task English_MatchesWordForms_WhenTheEngineStems()
    {
        if (!Capabilities().Stemming)
            return;

        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Running shoes"), Article("a2", null, body: "She runs every day"));

        (await IdsAsync(tenant, "run")).Should().BeEquivalentTo(["a1", "a2"]);
        (await IdsAsync(tenant, "shoe")).Should().Equal("a1");
    }

    [Fact]
    public async Task Every_word_must_match()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop"), Article("a2", "Office laptop"));

        (await IdsAsync(tenant, "gaming laptop")).Should().Equal("a1");
    }

    [Fact]
    public async Task A_phrase_matches_the_words_in_order_only()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Red lamp shade"), Article("a2", "Lamp, red"));

        (await IdsAsync(tenant, "\"red lamp\"")).Should().Equal("a1");
        (await IdsAsync(tenant, "red lamp")).Should().BeEquivalentTo(["a1", "a2"]);
    }

    [Fact]
    public async Task A_phrase_never_matches_across_two_fields()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop", code: "pro"), Article("a2", "Laptop pro edition"));

        (await IdsAsync(tenant, "\"laptop pro\"")).Should().Equal("a2");
    }

    [Fact]
    public async Task Excluded_words_and_alternatives()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("new", "Laptop new"), Article("used", "Laptop used"), Article("nb", "Notebook"));

        (await IdsAsync(tenant, "laptop -used")).Should().Equal("new");
        (await IdsAsync(tenant, "laptop or notebook")).Should().BeEquivalentTo(["new", "used", "nb"]);
    }

    [Fact]
    public async Task Codes_match_as_written_and_hyphenated_codes_as_a_phrase()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Lamp", code: "AB-1234"), Article("a2", "Lamp", code: "AB-9999"));

        (await IdsAsync(tenant, "ab-1234")).Should().Equal("a1");
        (await IdsAsync(tenant, "1234")).Should().Equal("a1");
    }

    [Fact]
    public async Task Autocomplete_matches_the_last_word_as_a_prefix()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop"), Article("a2", "Gaming chair"));

        (await IdsAsync(tenant, "gaming lap", mode: SearchMode.Autocomplete)).Should().Equal("a1");
        (await IdsAsync(tenant, "gaming lap", q => q with { FuzzyMinTermLength = null }))
            .Should().BeEmpty("full mode needs whole words (and fuzzy is off here)");
    }

    [Fact]
    public async Task A_misspelled_word_finds_close_spellings_and_says_so()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop"), Article("a2", "Desk lamp"));

        var result = await SearchAsync(tenant, "laptpo");

        result.Hits.Select(h => h.Id).Should().Equal("a1");
        result.MatchMode.Should().Be(SearchMatchMode.Fuzzy);
    }

    [Fact]
    public async Task No_close_spelling_when_fuzzy_is_off_or_words_are_too_short()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop"));

        (await IdsAsync(tenant, "laptpo", q => q with { FuzzyMinTermLength = null })).Should().BeEmpty();
        (await IdsAsync(tenant, "lx", q => q with { FuzzyMinTermLength = 3 })).Should().BeEmpty();
    }

    [Fact]
    public async Task An_exact_match_reports_exact()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Gaming laptop"));

        (await SearchAsync(tenant, "laptop")).MatchMode.Should().Be(SearchMatchMode.Exact);
        (await SearchAsync(tenant, null)).MatchMode.Should().Be(SearchMatchMode.Browse);
    }

    // ----- filters, facets, sort, paging ---------------------------------------------------------------------------

    [Fact]
    public async Task Every_filter_type_narrows_the_matches()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant,
            Article("a", "Lamp", status: "Published", tags: ["t1", "t2"], price: 10, publishedDay: 1, featured: true),
            Article("b", "Lamp", status: "Draft", tags: ["t2"], price: 20, publishedDay: 5, featured: false),
            Article("c", "Lamp", status: "Published", tags: ["t3"], price: 30, publishedDay: 9),
            Article("d", "Lamp", status: null, price: null));

        async Task<string[]> With(params SearchFilter[] filters) =>
            [.. (await IdsAsync(tenant, "lamp", q => q with { Filters = filters })).Order()];

        (await With(new KeywordFilter("status", "Published"))).Should().Equal("a", "c");
        (await With(new KeywordFilter("tag_ids", "t2"))).Should().Equal("a", "b");
        (await With(new KeywordsFilter("status", ["Draft", "Published"]))).Should().Equal("a", "b", "c");
        (await With(new KeywordsFilter("tag_ids", ["t1", "t3"]))).Should().Equal("a", "c");
        (await With(new KeywordsFilter("tag_ids", ["t1", "t2"], SearchMatch.All))).Should().Equal("a");
        (await With(new NumberRangeFilter("price", 15, null))).Should().Equal("b", "c");
        (await With(new NumberRangeFilter("price", null, 20))).Should().Equal("a", "b");
        (await With(new NumberRangeFilter("price", 10, 10))).Should().Equal("a");
        (await With(new DateRangeFilter("published_at", Epoch.AddDays(2), Epoch.AddDays(9)))).Should().Equal("b", "c");
        (await With(new BoolFilter("featured", true))).Should().Equal("a");
        (await With(new BoolFilter("featured", false))).Should().Equal("b");
        (await With(new KeywordFilter("status", "Published"), new NumberRangeFilter("price", 20, null))).Should().Equal("c");
    }

    [Fact]
    public async Task Facets_count_values_within_the_filtered_matches_most_frequent_first()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant,
            Article("a", "Lamp", status: "Published", tags: ["red", "big"], featured: true),
            Article("b", "Lamp", status: "Published", tags: ["red"], featured: false),
            Article("c", "Lamp", status: "Draft", tags: ["blue"]),
            Article("d", "Chair", status: "Published", tags: ["red"]));

        var result = await SearchAsync(tenant, "lamp", q => q with
        {
            Filters = [new KeywordFilter("status", "Published")],
            Facets = ["tag_ids", "featured", "status"]
        });

        result.Total.Should().Be(2);
        result.FacetsExhaustive.Should().BeTrue();
        result.Facets.Select(f => f.Field).Should().Equal("tag_ids", "featured", "status");
        result.Facets[0].Values.Should().Equal(new SearchFacetValue("red", 2), new SearchFacetValue("big", 1));
        result.Facets[1].Values.Should().BeEquivalentTo([new SearchFacetValue("false", 1), new SearchFacetValue("true", 1)]);
        result.Facets[2].Values.Should().Equal(new SearchFacetValue("Published", 2));
    }

    [Fact]
    public async Task Facets_return_at_most_the_configured_number_of_values()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Enumerable.Range(0, 5).Select(i => Article($"a{i}", "Lamp", tags: [$"t{i}", "common"])).ToArray());

        var facet = (await SearchAsync(tenant, "lamp", q => q with { Facets = ["tag_ids"], MaxFacetValues = 2 })).Facets.Single();

        facet.Values.Should().HaveCount(2);
        facet.Values[0].Should().Be(new SearchFacetValue("common", 5));
    }

    [Fact]
    public async Task Sorting_by_a_field_orders_the_matches_and_breaks_ties_by_id()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant,
            Article("b", "Lamp", price: 20), Article("a", "Lamp", price: 20), Article("c", "Lamp", price: 5), Article("d", "Lamp"));

        (await IdsAsync(tenant, "lamp", q => q with { Sort = new SearchSort("price") })).Should().Equal("c", "a", "b", "d");
        (await IdsAsync(tenant, "lamp", q => q with { Sort = new SearchSort("price", Descending: true) })).Should().Equal("a", "b", "c", "d");
        (await IdsAsync(tenant, "lamp", q => q with { Sort = new SearchSort("status") })).Should().Equal("a", "b", "c", "d");
    }

    [Fact]
    public async Task Browsing_without_text_uses_the_default_sort()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("old", "A", publishedDay: 1), Article("new", "B", publishedDay: 9), Article("mid", "C", publishedDay: 5));

        (await IdsAsync(tenant, null)).Should().Equal("new", "mid", "old");
    }

    [Fact]
    public async Task A_match_in_a_heavier_field_ranks_first()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant,
            Article("body", "Desk", body: "A desk for a laptop"),
            Article("title", "Laptop stand", body: "For the desk"));

        (await IdsAsync(tenant, "laptop")).Should().Equal("title", "body");
    }

    [Fact]
    public async Task Pages_neither_overlap_nor_skip_and_the_total_covers_all_pages()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Enumerable.Range(0, 7).Select(i => Article($"a{i}", "Lamp", price: i)).ToArray());

        var pages = new List<string>();
        for (var page = 0; page < 4; page++)
        {
            var result = await SearchAsync(tenant, "lamp", q => q with { Sort = new SearchSort("price"), Offset = page * 3, Limit = 3 });
            result.Total.Should().Be(7);
            pages.AddRange(result.Hits.Select(h => h.Id));
        }

        pages.Should().Equal("a0", "a1", "a2", "a3", "a4", "a5", "a6");
    }

    [Fact]
    public async Task Highlight_text_is_the_original_text_of_the_highlight_fields()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "مُحَمَّد's Laptop", body: "A light laptop", code: "LP-1"));

        var hit = (await SearchAsync(tenant, "laptop", q => q with { IncludeHighlightText = true })).Hits.Single();
        var plain = (await SearchAsync(tenant, "laptop")).Hits.Single();

        hit.HighlightText.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["title"] = "مُحَمَّد's Laptop",
            ["body"] = "A light laptop"
        });
        plain.HighlightText.Should().BeEmpty();
    }

    [Fact]
    public async Task Long_text_is_cut_to_the_fields_max_length_before_indexing()
    {
        var tenant = NewTenant();
        await IndexAsync(tenant, Article("a1", "Lamp", body: new string('x', 1996) + " zebra"));

        (await IdsAsync(tenant, "zebra")).Should().BeEmpty("the body field keeps its first 2000 characters only");
    }
}
