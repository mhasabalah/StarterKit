using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Search.Contracts.Querying;
using Search.Engine;
using Search.IntegrationTests.TestIndex;
using static Search.IntegrationTests.TestIndex.TestArticles;

namespace Search.IntegrationTests;

/// <summary><see cref="SearchDocumentsQuery"/> through the mediator, on the real module and the PostgreSQL engine.</summary>
[Collection(SearchPostgresCollection.Name)]
public sealed class SearchDocumentsQueryTests(SearchPostgresFixture fixture)
{
    private async Task<Guid> TenantWithArticlesAsync(params Contracts.Indexing.SearchDocument[] articles)
    {
        var tenant = Guid.NewGuid();
        await fixture.RunAsync(tenant, async (ISearchEngine engine) =>
        {
            await engine.UpsertAsync(new IndexScope(fixture.Index, tenant),
                articles.Select(a => SearchDocumentPreparer.Prepare(fixture.Index, a)).ToList(), 1, CancellationToken.None);
            return true;
        });
        return tenant;
    }

    private static SearchDocumentsQuery Query(string? text) => new() { IndexKey = Key, Text = text };

    [Fact]
    public async Task An_index_that_was_never_built_is_not_ready()
    {
        var tenant = await TenantWithArticlesAsync(Article("a1", "Laptop"));

        var result = await fixture.SendAsync(tenant, Query("laptop") with { Page = 2, PageSize = 5 });

        result.Status.Should().Be(SearchStatus.IndexNotReady);
        result.Hits.Should().BeEmpty();
        (result.Page, result.PageSize).Should().Be((2, 5));
    }

    [Fact]
    public async Task A_built_index_answers_with_hits_totals_and_highlights_of_the_original_text()
    {
        var tenant = await TenantWithArticlesAsync(
            Article("a1", "Gaming Laptop", body: "A light laptop for travel", price: 900),
            Article("a2", "Laptop bag", price: 50),
            Article("a3", "Desk lamp"));
        await fixture.MarkBuiltAsync(tenant);

        var result = await fixture.SendAsync(tenant, Query("laptop") with
        {
            Highlight = true,
            Sort = new SearchSort("price", Descending: true),
            PageSize = 1
        });

        result.Status.Should().Be(SearchStatus.Ok);
        result.MatchMode.Should().Be(SearchMatchMode.Exact);
        result.Total.Should().Be(2);
        result.Hits.Should().ContainSingle().Which.Id.Should().Be("a1");
        result.Hits[0].Highlights.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["title"] = "Gaming <mark>Laptop</mark>",
            ["body"] = "A light <mark>laptop</mark> for travel"
        });
    }

    [Fact]
    public async Task Without_text_the_query_browses_and_returns_no_highlights()
    {
        var tenant = await TenantWithArticlesAsync(Article("old", "A", publishedDay: 1), Article("new", "B", publishedDay: 2));
        await fixture.MarkBuiltAsync(tenant);

        var result = await fixture.SendAsync(tenant, Query(null) with { Highlight = true });

        result.MatchMode.Should().Be(SearchMatchMode.Browse);
        result.Hits.Select(h => h.Id).Should().Equal("new", "old");
        result.Hits.Should().OnlyContain(h => h.Highlights.Count == 0);
    }

    [Fact]
    public async Task The_callers_branch_scope_applies()
    {
        var (north, south) = (Guid.NewGuid(), Guid.NewGuid());
        var tenant = await TenantWithArticlesAsync(
            Article("wide", "Laptop"), Article("north", "Laptop", branch: north), Article("south", "Laptop", branch: south));
        await fixture.MarkBuiltAsync(tenant);

        var result = await fixture.SendAsync(tenant, Query("laptop"), branches: [north]);

        result.Hits.Select(h => h.Id).Should().BeEquivalentTo(["wide", "north"]);
        result.Total.Should().Be(2);
    }

    [Fact]
    public async Task Fuzzy_can_be_turned_off_per_query()
    {
        var tenant = await TenantWithArticlesAsync(Article("a1", "Gaming laptop"));
        await fixture.MarkBuiltAsync(tenant);

        (await fixture.SendAsync(tenant, Query("laptpo"))).MatchMode.Should().Be(SearchMatchMode.Fuzzy);
        (await fixture.SendAsync(tenant, Query("laptpo") with { Fuzzy = false })).Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task An_engine_that_does_not_answer_makes_the_owner_fall_back()
    {
        var tenant = Guid.NewGuid();
        await fixture.MarkBuiltAsync(tenant);
        var down = new Mock<ISearchEngine>();
        down.Setup(e => e.SearchAsync(It.IsAny<IndexScope>(), It.IsAny<EngineQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SearchEngineUnavailableException("Postgres", new TimeoutException("too slow")));
        await using var services = fixture.BuildProvider(s => s.AddScoped(_ => down.Object));

        var result = await fixture.SendAsync(tenant, Query("laptop"), services: services);

        result.Status.Should().Be(SearchStatus.EngineUnavailable);
        result.Hits.Should().BeEmpty();
    }

    [Fact]
    public async Task A_bug_in_the_engine_is_not_hidden_as_unavailable()
    {
        var tenant = Guid.NewGuid();
        await fixture.MarkBuiltAsync(tenant);
        var broken = new Mock<ISearchEngine>();
        broken.Setup(e => e.SearchAsync(It.IsAny<IndexScope>(), It.IsAny<EngineQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bad SQL"));
        await using var services = fixture.BuildProvider(s => s.AddScoped(_ => broken.Object));

        await FluentActions.Invoking(() => fixture.SendAsync(tenant, Query("laptop"), services: services))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("bad SQL");
    }

    [Fact]
    public async Task An_invalid_query_is_rejected_before_the_engine()
    {
        var tenant = Guid.NewGuid();

        await FluentActions.Invoking(() => fixture.SendAsync(tenant, Query("x") with { IndexKey = "test.missing" }))
            .Should().ThrowAsync<ValidationException>();
        await FluentActions.Invoking(() => fixture.SendAsync(tenant, Query("x") with { Sort = new SearchSort("tag_ids") }))
            .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Rows_are_isolated_by_row_level_security_too()
    {
        var a = await TenantWithArticlesAsync(Article("a1", "Laptop"));
        var b = await TenantWithArticlesAsync(Article("b1", "Laptop"));

        // As the application role, inside tenant A's scope, an unfiltered read sees only tenant A's rows.
        var visible = await fixture.RunAsync(a, async sp =>
            await sp.GetRequiredService<Data.SearchDbContext>().Documents.IgnoreQueryFilters()
                .Where(d => d.TenantId == a || d.TenantId == b).Select(d => d.DocumentId).ToListAsync());

        visible.Should().Equal("a1");
        (await fixture.AdminScalarAsync<long>(
            $"SELECT count(*) FROM search.search_documents WHERE tenant_id IN ('{a}', '{b}')")).Should().Be(2);
    }
}
