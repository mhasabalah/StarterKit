using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Search.Contracts.Querying;
using Search.Domains.Analytics.Jobs;
using Search.Domains.Analytics.Services;
using Search.Engine;
using static Search.IntegrationTests.TestIndex.TestArticles;

namespace Search.IntegrationTests;

/// <summary>
/// Zero-result analytics end to end on the real module: searches through the mediator are counted in memory, the flush
/// writes them per tenant through the app role (RLS), the analytics queries read them back, and the purge removes old
/// days. Each test uses fresh tenants and its own search words.
/// </summary>
[Collection(SearchPostgresCollection.Name)]
public sealed class SearchAnalyticsTests(SearchPostgresFixture fixture)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<Guid> BuiltTenantWithAsync(params Contracts.Indexing.SearchDocument[] articles)
    {
        var tenant = Guid.NewGuid();
        await fixture.RunAsync(tenant, async (ISearchEngine engine) =>
        {
            await engine.UpsertAsync(new IndexScope(fixture.Index, tenant),
                articles.Select(a => SearchDocumentPreparer.Prepare(fixture.Index, a)).ToList(), 1, CancellationToken.None);
            return true;
        });
        await fixture.MarkBuiltAsync(tenant);
        return tenant;
    }

    private Task<SearchDocumentsResult> SearchAsync(Guid tenant, string? text, SearchMode mode = SearchMode.Full, int page = 1) =>
        fixture.SendAsync(tenant, new SearchDocumentsQuery { IndexKey = Key, Text = text, Mode = mode, Page = page });

    private Task<int> FlushAsync() =>
        fixture.Services.GetServices<IHostedService>().OfType<SearchAnalyticsFlushService>().Single()
            .FlushAsync(CancellationToken.None);

    private async Task<SearchDailyStatsDto?> TodayAsync(Guid tenant) =>
        (await fixture.SendAsync(tenant, new GetSearchDailyStatsQuery())).SingleOrDefault(s => s.Day == Today);

    [Fact]
    public async Task Searches_are_counted_per_outcome_and_zero_result_terms_are_kept_normalized()
    {
        var tenant = await BuiltTenantWithAsync(Article("a1", "Gaming laptop"));

        await SearchAsync(tenant, "laptop");                       // found
        await SearchAsync(tenant, "Wireless  MOUSE");              // nothing
        await SearchAsync(tenant, "wireless mouse");               // nothing, same normalized term
        await SearchAsync(tenant, "laptpo");                       // close spelling
        await FlushAsync();

        var today = await TodayAsync(tenant);
        today.Should().BeEquivalentTo(new { IndexKey = Key, Searches = 4, ZeroResults = 2, FuzzyRescues = 1, Unanswered = 0 });
        today!.ZeroResultRate.Should().Be(0.5);

        var terms = await fixture.SendAsync(tenant, new GetSearchZeroResultTermsQuery());
        terms.Result.Should().ContainSingle().Which.Should().BeEquivalentTo(new { IndexKey = Key, Term = "wireless mouse", Count = 2 });
        terms.PageInfoResult.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Only_submitted_text_searches_count_not_autocomplete_paging_or_browsing()
    {
        var tenant = await BuiltTenantWithAsync(Article("a1", "Gaming laptop"));

        await SearchAsync(tenant, "lap", SearchMode.Autocomplete);
        await SearchAsync(tenant, "laptop", page: 2);
        await SearchAsync(tenant, null);
        await SearchAsync(tenant, "-laptop");                      // exclusion only: no text to search
        await FlushAsync();

        (await TodayAsync(tenant)).Should().BeNull();
    }

    [Fact]
    public async Task A_search_the_index_cannot_answer_is_counted_as_unanswered_without_a_term()
    {
        var tenant = Guid.NewGuid(); // never built

        await SearchAsync(tenant, "anything at all");
        await FlushAsync();

        (await TodayAsync(tenant)).Should().BeEquivalentTo(new { Searches = 1, ZeroResults = 0, Unanswered = 1 });
        (await TodayAsync(tenant))!.ZeroResultRate.Should().Be(0);
        (await fixture.SendAsync(tenant, new GetSearchZeroResultTermsQuery())).Result.Should().BeEmpty();
    }

    [Fact]
    public async Task Terms_that_could_identify_a_person_are_counted_but_never_stored()
    {
        var tenant = await BuiltTenantWithAsync(Article("a1", "Gaming laptop"));

        await SearchAsync(tenant, "someone@example.com");
        await SearchAsync(tenant, "+20 100 123 4567");
        await SearchAsync(tenant, "x");
        await FlushAsync();

        (await TodayAsync(tenant))!.ZeroResults.Should().Be(3);
        (await fixture.SendAsync(tenant, new GetSearchZeroResultTermsQuery())).Result.Should().BeEmpty();
        (await fixture.AdminScalarAsync<long>(
                $"SELECT count(*) FROM search.search_zero_result_terms WHERE tenant_id = '{tenant}'"))
            .Should().Be(0);
    }

    [Fact]
    public async Task Counts_of_several_flushes_add_up_and_each_tenant_sees_only_its_own()
    {
        var acme = await BuiltTenantWithAsync(Article("a1", "Laptop"));
        var globex = await BuiltTenantWithAsync(Article("g1", "Laptop"));

        await SearchAsync(acme, "standing desk");
        await FlushAsync();
        await SearchAsync(acme, "standing desk");
        await SearchAsync(globex, "office chair");
        await FlushAsync();

        (await fixture.SendAsync(acme, new GetSearchZeroResultTermsQuery())).Result
            .Should().ContainSingle().Which.Should().BeEquivalentTo(new { Term = "standing desk", Count = 2 });
        (await fixture.SendAsync(globex, new GetSearchZeroResultTermsQuery())).Result
            .Should().ContainSingle().Which.Term.Should().Be("office chair");
        (await TodayAsync(acme))!.Searches.Should().Be(2);
        (await TodayAsync(globex))!.Searches.Should().Be(1);
    }

    [Fact]
    public async Task Zero_result_terms_are_summed_over_the_range_most_frequent_first_and_paged()
    {
        var tenant = Guid.NewGuid();
        await fixture.AdminExecAsync($"""
            INSERT INTO search.search_zero_result_terms (tenant_id, index_key, "day", term, "count", last_seen_at) VALUES
            ('{tenant}', '{Key}', '{Today.AddDays(-1):yyyy-MM-dd}', 'rare', 1, now()),
            ('{tenant}', '{Key}', '{Today.AddDays(-2):yyyy-MM-dd}', 'common', 3, now()),
            ('{tenant}', '{Key}', '{Today.AddDays(-1):yyyy-MM-dd}', 'common', 4, now()),
            ('{tenant}', '{Key}', '{Today.AddDays(-40):yyyy-MM-dd}', 'old', 50, now())
            """);

        var first = await fixture.SendAsync(tenant, new GetSearchZeroResultTermsQuery { PageSize = 1 });
        first.Result.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Term = "common", Count = 7 });
        first.PageInfoResult.TotalItems.Should().Be(2, "'old' is outside the default 30 days");

        var all = await fixture.SendAsync(tenant, new GetSearchZeroResultTermsQuery { From = Today.AddDays(-60), To = Today });
        all.Result!.Select(t => t.Term).Should().Equal("old", "common", "rare");

        (await fixture.SendAsync(tenant, new GetSearchZeroResultTermsQuery { IndexKey = Key, From = Today.AddDays(-1), To = Today.AddDays(-1) }))
            .Result!.Select(t => (t.Term, t.Count)).Should().Equal(("common", 4), ("rare", 1));
    }

    [Fact]
    public async Task An_analytics_query_with_a_bad_range_or_an_unknown_index_is_rejected()
    {
        var tenant = Guid.NewGuid();

        await FluentActions.Awaiting(() => fixture.SendAsync(tenant,
                new GetSearchDailyStatsQuery { From = Today, To = Today.AddDays(-1) }))
            .Should().ThrowAsync<ValidationException>();
        await FluentActions.Awaiting(() => fixture.SendAsync(tenant,
                new GetSearchDailyStatsQuery { From = Today.AddDays(-GetSearchDailyStatsQuery.MaxDays), To = Today }))
            .Should().ThrowAsync<ValidationException>();
        await FluentActions.Awaiting(() => fixture.SendAsync(tenant, new GetSearchZeroResultTermsQuery { IndexKey = "test.removed" }))
            .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task The_purge_removes_the_days_past_the_retention_only()
    {
        var tenant = SearchPostgresFixture.TenantA.Id; // the purge visits the serviceable tenants
        var old = Today.AddDays(-91).ToString("yyyy-MM-dd");
        var kept = Today.AddDays(-89).ToString("yyyy-MM-dd");
        await fixture.AdminExecAsync($"""
            INSERT INTO search.search_daily_stats (tenant_id, index_key, "day", searches, zero_results, fuzzy_rescues, unanswered, updated_at)
            VALUES ('{tenant}', 'test.purge', '{old}', 1, 0, 0, 0, now()), ('{tenant}', 'test.purge', '{kept}', 1, 0, 0, 0, now());
            INSERT INTO search.search_zero_result_terms (tenant_id, index_key, "day", term, "count", last_seen_at)
            VALUES ('{tenant}', 'test.purge', '{old}', 'gone', 1, now()), ('{tenant}', 'test.purge', '{kept}', 'kept', 1, now());
            """);

        await using (var scope = fixture.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<PurgeSearchAnalyticsJob>().ExecuteAsync(null)).Should().BeGreaterThanOrEqualTo(2);

        (await fixture.AdminScalarAsync<string>(
                $"SELECT string_agg(\"day\"::text, ',') FROM search.search_daily_stats WHERE tenant_id = '{tenant}' AND index_key = 'test.purge'"))
            .Should().Be(kept);
        (await fixture.AdminScalarAsync<string>(
                $"SELECT string_agg(term, ',') FROM search.search_zero_result_terms WHERE tenant_id = '{tenant}' AND index_key = 'test.purge'"))
            .Should().Be("kept");
    }
}
