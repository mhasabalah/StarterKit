using Catalog.Contracts.Products.Commands;
using Catalog.Contracts.Products.Enums;
using Catalog.Contracts.Products.Queries;
using Catalog.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Search.Domains.Indexes.Jobs;
using Shared.Contracts.Enums;
using Shared.Contracts.Localization;
using Shared.Core.Messaging.Outbox;

namespace Catalog.IntegrationTests.Search;

/// <summary>
/// Product search on the search index, end to end: products created through the real commands, indexed by the real
/// Search module (rebuild, or the outbox events), searched through the Catalog queries. Each test isolates its products
/// with a fresh owner (<c>OwnerId</c> filter).
/// </summary>
[Collection(CatalogSearchCollection.Name)]
public sealed class ProductIndexedSearchTests(CatalogSearchFixture fx)
{
    private readonly Scenario _s = new(fx);

    private Task<Contracts.Products.Dtos.SearchProductsResult> SearchAsync(string term, Func<SearchProductsQuery, SearchProductsQuery>? with = null)
    {
        var query = new SearchProductsQuery { SearchTerm = term, OwnerId = _s.Owner, PageSize = 20 };
        return fx.SendAs(null, with is null ? query : with(query));
    }

    private async Task IndexAsync() =>
        (await fx.RebuildAsync()).Outcome.Should().Be(RebuildOutcome.Built);

    [Fact]
    public async Task The_index_ranks_names_first_and_matches_word_forms_not_substrings()
    {
        var inName = await _s.Published("Running shoes");
        var inDescription = await _s.Published("Trail pack", description: "For running in the hills");
        await _s.Published("Brunch table"); // contains "run" as a substring, not as a word
        await IndexAsync();

        var result = await SearchAsync("run");

        result.Match.Should().Be(ProductSearchMatch.Exact);
        result.Result!.Select(p => p.Id).Should().Equal(inName, inDescription);
        result.PageInfoResult.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task Arabic_letter_forms_and_diacritics_are_normalized()
    {
        var id = await _s.Create("Sneakers", "أحذيةٌ رياضية");
        await _s.Publish(id);
        await IndexAsync();

        (await SearchAsync("احذيه رياضيه")).Result!.Select(p => p.Id).Should().Equal(id);
        (await fx.SendAs(null, new GetProductsQuery { Search = "احذية", OwnerId = _s.Owner })).Result!
            .Select(p => p.Id).Should().Equal([id], "the public list answers its text search from the index too");
    }

    [Fact]
    public async Task A_misspelling_finds_close_spellings()
    {
        var id = await _s.Published("Ergonomic keyboard");
        await IndexAsync();

        var result = await SearchAsync("keybaord");

        result.Match.Should().Be(ProductSearchMatch.Fuzzy);
        result.Result!.Select(p => p.Id).Should().Equal(id);
    }

    [Fact]
    public async Task Facets_count_the_matches_and_filters_narrow_them()
    {
        var token = Token();
        var a = await _s.Published($"Lamp {token} A", type: ProductType.Physical,
            tags: [CatalogDataSeeder.NewArrivalTagId, CatalogDataSeeder.BestSellerTagId]);
        await _s.Published($"Lamp {token} B", type: ProductType.Physical, tags: [CatalogDataSeeder.NewArrivalTagId]);
        await _s.Published($"Lamp {token} C", type: ProductType.Digital);
        await IndexAsync();

        var result = await SearchAsync(token, q => q with { Facets = [ProductFacet.Type, ProductFacet.Tag] });

        result.Facets.Single(f => f.Facet == ProductFacet.Type).Values
            .Should().BeEquivalentTo([new { Value = "Physical", Count = 2 }, new { Value = "Digital", Count = 1 }]);
        result.Facets.Single(f => f.Facet == ProductFacet.Tag).Values
            .Should().ContainEquivalentOf(new { Value = CatalogDataSeeder.NewArrivalTagId.ToString(), Count = 2 });
        result.FacetsExhaustive.Should().BeTrue();

        var narrowed = await SearchAsync(token, q => q with
        {
            TagIds = [CatalogDataSeeder.NewArrivalTagId, CatalogDataSeeder.BestSellerTagId]
        });
        narrowed.Result!.Select(p => p.Id).Should().Equal(a);
    }

    [Fact]
    public async Task Highlights_mark_the_matches_in_the_name_and_description()
    {
        var id = await _s.Published("Walnut desk", description: "A solid walnut desk for <small> rooms");
        await IndexAsync();

        var result = await SearchAsync("walnut", q => q with { Highlight = true });

        var highlights = result.Highlights[id];
        highlights["nameEn"].Should().Be("<mark>Walnut</mark> desk");
        highlights["description"].Should().Contain("<mark>walnut</mark>").And.Contain("&lt;small&gt;");
        (await SearchAsync("walnut")).Highlights.Should().BeEmpty("highlights are opt-in");
    }

    [Fact]
    public async Task Changes_reach_the_index_through_the_outbox_one_event_per_save()
    {
        await IndexAsync();
        var token = Token();
        var id = await _s.Create($"Lantern {token}");
        (await SearchEventsFor(id)).Should().Be(1, "create raises one event");

        await _s.Publish(id);
        await fx.DeliverSearchEventsAsync();
        (await SearchAsync(token)).Result!.Select(p => p.Id).Should().Equal(id);

        var version = await fx.WithDb(db => db.Products.Where(p => p.Id == id).Select(p => p.Version).SingleAsync());
        var before = await SearchEventsFor(id);
        await fx.SendAsAdmin(new UpdateProductCommand(id, LocalizedText.Create($"Torch {token}", "مصباح"), null, // admins edit live products
            Scenario.UniqueSku(), ProductType.Physical, 12m, null, Currency.USD, null,
            [CatalogDataSeeder.NewArrivalTagId], [], version));
        (await SearchEventsFor(id)).Should().Be(before + 1, "an update that also changes tags is still one save, one event");

        await fx.DeliverSearchEventsAsync();
        (await SearchAsync("lantern")).Result.Should().BeEmpty("the old name is gone from the index");
        (await SearchAsync($"torch {token}")).Result!.Select(p => p.Id).Should().Equal(id);

        await fx.SendAs(_s.Owner, new DeleteProductCommand(id));
        await fx.DeliverSearchEventsAsync();
        (await SearchAsync(token)).PageInfoResult.TotalItems.Should().Be(0, "a deleted product leaves the index");
    }

    [Fact]
    public async Task What_the_index_still_holds_is_re_checked_in_SQL_before_it_is_shown()
    {
        var token = Token();
        var id = await _s.Published($"Kettle {token}");
        await IndexAsync();

        // Archived, and the change not delivered yet: the index still says Published.
        var version = await fx.WithDb(db => db.Products.Where(p => p.Id == id).Select(p => p.Version).SingleAsync());
        await fx.SendAs(_s.Owner, new ArchiveProductCommand(id, version));

        var result = await SearchAsync(token);

        result.Result.Should().BeEmpty("hydration re-applies the Published filter");
    }

    [Fact]
    public async Task A_sort_the_index_cannot_serve_is_answered_by_SQL()
    {
        var token = Token();
        var id = await _s.Published($"Vase {token}");
        await IndexAsync();

        var bySql = await SearchAsync(token, q => q with { SortColumn = "name", SortBy = SortBy.Ascending });
        var byIndex = await SearchAsync(token, q => q with { SortColumn = "price", SortBy = SortBy.Ascending });

        bySql.Match.Should().Be(ProductSearchMatch.Contains);
        bySql.Result!.Select(p => p.Id).Should().Equal(id);
        byIndex.Match.Should().Be(ProductSearchMatch.Exact);
        byIndex.Result!.Select(p => p.Id).Should().Equal(id);
    }

    [Fact]
    public async Task Price_sort_and_paging_come_from_the_index_with_the_index_total()
    {
        var token = Token();
        var cheap = await _s.Published($"Mug {token}", price: 5m);
        var mid = await _s.Published($"Mug {token} large", price: 15m);
        var dear = await _s.Published($"Mug {token} set", price: 25m);
        await IndexAsync();

        var page1 = await SearchAsync(token, q => q with { SortColumn = "Price", SortBy = SortBy.Descending, PageSize = 2 });
        var page2 = await SearchAsync(token, q => q with { SortColumn = "Price", SortBy = SortBy.Descending, PageSize = 2, PageNumber = 2 });

        page1.Result!.Select(p => p.Id).Should().Equal(dear, mid);
        page2.Result!.Select(p => p.Id).Should().Equal(cheap);
        page1.PageInfoResult.TotalItems.Should().Be(3);
    }

    [Fact]
    public async Task Suggestions_match_the_word_being_typed_on_published_products_only()
    {
        var token = Token();
        var live = await _s.Published($"Headphones {token}");
        var draft = await _s.Create($"Headset {token}");
        await IndexAsync();

        var suggestions = await fx.SendAs(null, new SuggestProductsQuery { Q = $"{token} headp" });

        var suggestion = suggestions.Should().ContainSingle().Which;
        suggestion.Id.Should().Be(live);
        suggestion.Highlight.Should().Contain("<mark>Headphones</mark>");
        (await fx.SendAs(null, new SuggestProductsQuery { Q = $"{token} heads" }))
            .Should().NotContain(s => s.Id == draft, "drafts are never suggested, even when their name matches exactly");
    }

    [Fact]
    public async Task The_admin_list_with_deleted_products_stays_on_SQL()
    {
        var token = Token();
        var id = await _s.Published($"Stool {token}");
        await IndexAsync();
        await fx.SendAs(_s.Owner, new DeleteProductCommand(id));

        var page = await fx.SendAsAdmin(new GetAdminProductsQuery { Search = token, IncludeDeleted = true });

        page.Result!.Select(p => p.Id).Should().Equal(id);
    }

    private Task<int> SearchEventsFor(Guid id) =>
        fx.WithDb(async db => (await db.Set<TenantOutboxMessage>().AsNoTracking()
                .Where(m => m.MessageType == "urn:message:search.documents-changed.v1")
                .Select(m => m.Payload)
                .ToListAsync())
            .Count(payload => payload.Contains(id.ToString(), StringComparison.Ordinal)));

    /// <summary>A word no other product has (letters only, so every analyzer keeps it whole).</summary>
    private static string Token() =>
        "q" + new string(Guid.NewGuid().ToString("N").Select(c => (char)('a' + (Convert.ToInt32(c.ToString(), 16) % 26))).Take(9).ToArray());
}
