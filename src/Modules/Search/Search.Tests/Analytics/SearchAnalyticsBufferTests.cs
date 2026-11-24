using Search.Contracts.Querying;
using Search.Domains.Analytics.Services;
using Search.Engine.Text;
using Search.Options;

namespace Search.Tests.Analytics;

public sealed class SearchAnalyticsBufferTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly DateTime Noon = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    private static SearchAnalyticsBuffer Buffer(int maxPendingTerms = 10_000) =>
        new(Microsoft.Extensions.Options.Options.Create(new SearchOptions
        {
            Analytics = new SearchAnalyticsOptions { MaxPendingTerms = maxPendingTerms }
        }));

    [Fact]
    public void Searches_are_counted_per_tenant_index_and_day()
    {
        var buffer = Buffer();
        buffer.Record(TenantA, "a.items", SearchOutcome.Found, null, Noon);
        buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, "lamp", Noon);
        buffer.Record(TenantA, "a.items", SearchOutcome.FuzzyRescue, null, Noon);
        buffer.Record(TenantA, "a.items", SearchOutcome.Unanswered, null, Noon);
        buffer.Record(TenantA, "a.items", SearchOutcome.Found, null, Noon.AddDays(1));
        buffer.Record(TenantB, "a.items", SearchOutcome.Found, null, Noon);

        var batch = buffer.Drain();

        batch.Daily.Should().HaveCount(3);
        batch.Daily.Single(d => d.TenantId == TenantA && d.Day == DateOnly.FromDateTime(Noon))
            .Should().BeEquivalentTo(new { Searches = 4, ZeroResults = 1, FuzzyRescues = 1, Unanswered = 1 });
    }

    [Fact]
    public void Zero_result_terms_are_counted_with_their_last_time_and_only_for_zero_results()
    {
        var buffer = Buffer();
        buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, "lamp", Noon);
        buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, "lamp", Noon.AddMinutes(5));
        buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, null, Noon);          // scrubbed away
        buffer.Record(TenantA, "a.items", SearchOutcome.Found, "desk", Noon);

        var term = buffer.Drain().ZeroResults.Should().ContainSingle().Which;

        term.Should().BeEquivalentTo(new { Term = "lamp", Count = 2, LastSeenAt = Noon.AddMinutes(5) });
    }

    [Fact]
    public void A_full_buffer_drops_new_terms_but_keeps_counting_known_ones_and_every_search()
    {
        var buffer = Buffer(maxPendingTerms: 100);
        for (var i = 0; i < 100; i++)
            buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, $"term {i}", Noon);

        buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, "one too many", Noon);
        buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, "term 0", Noon);

        var batch = buffer.Drain();
        batch.ZeroResults.Should().HaveCount(100).And.NotContain(t => t.Term == "one too many");
        batch.ZeroResults.Single(t => t.Term == "term 0").Count.Should().Be(2);
        batch.DroppedTerms.Should().Be(1);
        batch.Daily.Single().ZeroResults.Should().Be(102);
    }

    [Fact]
    public void Drain_takes_everything_and_starts_empty()
    {
        var buffer = Buffer();
        buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, "lamp", Noon);

        buffer.Drain().IsEmpty.Should().BeFalse();
        buffer.Drain().Should().BeEquivalentTo(new { IsEmpty = true, DroppedTerms = 0 });
    }

    [Fact]
    public async Task Concurrent_searches_are_all_counted()
    {
        var buffer = Buffer();
        var drained = new List<SearchAnalyticsBatch>();

        var writers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++)
                buffer.Record(TenantA, "a.items", SearchOutcome.ZeroResults, "lamp", Noon);
        })).ToList();
        while (!writers.TrueForAll(w => w.IsCompleted))
        {
            drained.Add(buffer.Drain());
            await Task.Yield();
        }

        await Task.WhenAll(writers);
        drained.Add(buffer.Drain());

        drained.SelectMany(b => b.Daily).Sum(d => d.Searches).Should().Be(8000);
        drained.SelectMany(b => b.ZeroResults).Sum(t => t.Count).Should().Be(8000);
    }
}

public sealed class SearchAnalyticsRecorderTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static (SearchAnalyticsRecorder Recorder, SearchAnalyticsBuffer Buffer) Create(bool enabled = true)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new SearchOptions
        {
            Analytics = new SearchAnalyticsOptions { Enabled = enabled, MaxQueryLength = 64 }
        });
        var buffer = new SearchAnalyticsBuffer(options);
        return (new SearchAnalyticsRecorder(buffer, options, TimeProvider.System), buffer);
    }

    private static SearchDocumentsQuery Query(string? text, SearchMode mode = SearchMode.Full, int page = 1) =>
        new() { IndexKey = "a.items", Text = text, Mode = mode, Page = page };

    private static SearchDocumentsResult Result(int total, SearchMatchMode mode = SearchMatchMode.Exact) => new()
    {
        Status = SearchStatus.Ok, Total = total, MatchMode = mode, Page = 1, PageSize = 20
    };

    private static void Record(SearchAnalyticsRecorder recorder, SearchDocumentsQuery query, SearchDocumentsResult result) =>
        recorder.Record(Tenant, query, SearchQueryParser.Parse(query.Text, query.Mode, 10), result);

    public static TheoryData<SearchDocumentsResult, SearchOutcome> Outcomes => new()
    {
        { Result(3), SearchOutcome.Found },
        { Result(0), SearchOutcome.ZeroResults },
        { Result(2, SearchMatchMode.Fuzzy), SearchOutcome.FuzzyRescue },
        { SearchDocumentsResult.Unavailable(SearchStatus.IndexNotReady, 1, 20), SearchOutcome.Unanswered },
        { SearchDocumentsResult.Unavailable(SearchStatus.EngineUnavailable, 1, 20), SearchOutcome.Unanswered }
    };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public void A_submitted_search_is_counted_by_its_outcome(SearchDocumentsResult result, SearchOutcome outcome)
    {
        var (recorder, buffer) = Create();

        Record(recorder, Query("Desk Lamp"), result);

        var batch = buffer.Drain();
        var daily = batch.Daily.Should().ContainSingle().Which;
        daily.Searches.Should().Be(1);
        (daily.ZeroResults, daily.FuzzyRescues, daily.Unanswered).Should().Be((
            outcome == SearchOutcome.ZeroResults ? 1 : 0,
            outcome == SearchOutcome.FuzzyRescue ? 1 : 0,
            outcome == SearchOutcome.Unanswered ? 1 : 0));
        batch.ZeroResults.Select(t => t.Term).Should().Equal(outcome == SearchOutcome.ZeroResults ? ["desk lamp"] : []);
    }

    public static TheoryData<SearchDocumentsQuery> NotSubmitted => new()
    {
        Query("lam", SearchMode.Autocomplete),
        Query("lamp", page: 2),
        Query(null),
        Query("   "),
        Query("-lamp")
    };

    [Theory]
    [MemberData(nameof(NotSubmitted))]
    public void Keystrokes_paging_and_browsing_are_not_counted(SearchDocumentsQuery query)
    {
        var (recorder, buffer) = Create();

        Record(recorder, query, Result(0));

        buffer.Drain().IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Nothing_is_counted_when_analytics_are_off()
    {
        var (recorder, buffer) = Create(enabled: false);

        Record(recorder, Query("lamp"), Result(0));

        buffer.Drain().IsEmpty.Should().BeTrue();
    }
}
