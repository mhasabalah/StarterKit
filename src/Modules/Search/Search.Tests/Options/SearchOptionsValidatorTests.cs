using Search.Options;

namespace Search.Tests.Options;

public sealed class SearchOptionsValidatorTests
{
    private static readonly SearchOptionsValidator Validator = new();

    private static IEnumerable<string> Failures(Action<SearchOptions> change)
    {
        var options = new SearchOptions();
        change(options);
        return Validator.Validate(null, options).Failures ?? [];
    }

    [Fact]
    public void The_defaults_are_valid() =>
        Validator.Validate(null, new SearchOptions()).Succeeded.Should().BeTrue();

    [Fact]
    public void An_undefined_engine_is_rejected() =>
        Failures(o => o.Engine = (SearchEngineKind)9).Should().ContainSingle().Which.Should().Contain("Engine");

    [Fact]
    public void Meilisearch_needs_a_url_and_an_api_key() =>
        Failures(o => o.Engine = SearchEngineKind.Meilisearch).Should().HaveCount(2)
            .And.Contain(f => f.Contains("Meilisearch:Url"))
            .And.Contain(f => f.Contains("Meilisearch:ApiKey"));

    [Theory]
    [InlineData("meilisearch:7700")]
    [InlineData("ftp://meilisearch:7700")]
    [InlineData("/relative")]
    public void Meilisearch_needs_an_absolute_http_url(string url) =>
        Failures(o =>
            {
                o.Engine = SearchEngineKind.Meilisearch;
                o.Meilisearch.Url = url;
                o.Meilisearch.ApiKey = "key";
            })
            .Should().ContainSingle().Which.Should().Contain("Meilisearch:Url");

    [Fact]
    public void A_configured_meilisearch_is_valid() =>
        Failures(o =>
            {
                o.Engine = SearchEngineKind.Meilisearch;
                o.Meilisearch.Url = "http://meilisearch:7700";
                o.Meilisearch.ApiKey = "key";
            })
            .Should().BeEmpty();

    [Fact]
    public void Meilisearch_settings_are_not_required_on_postgres() =>
        Failures(o => o.Meilisearch.Url = "not a url").Should().BeEmpty();

    [Fact]
    public void The_index_prefix_must_be_a_safe_name() =>
        Failures(o => o.Meilisearch.IndexPrefix = "prod/").Should().ContainSingle().Which.Should().Contain("IndexPrefix");

    public static TheoryData<string, Action<SearchOptions>> OutOfRange => new()
    {
        { "Indexing:BatchSize", o => o.Indexing.BatchSize = 0 },
        { "Indexing:LeaseMinutes", o => o.Indexing.LeaseMinutes = 0 },
        { "Query:MaxPageSize", o => o.Query.MaxPageSize = 0 },
        { "Query:MaxWindow", o => o.Query.MaxWindow = 10 },
        { "Query:MaxTextLength", o => o.Query.MaxTextLength = 0 },
        { "Query:MaxTerms", o => o.Query.MaxTerms = 0 },
        { "Query:Fuzzy:MinTermLength", o => o.Query.Fuzzy.MinTermLength = 0 },
        { "Query:Fuzzy:Threshold", o => o.Query.Fuzzy.Threshold = 0 },
        { "Facets:MaxValues", o => o.Facets.MaxValues = 0 },
        { "Facets:ScanLimit", o => o.Facets.ScanLimit = 0 },
        { "Postgres:StatementTimeoutMs", o => o.Postgres.StatementTimeoutMs = 10 },
        { "Reconcile:Cron", o => o.Reconcile.Cron = "often" },
        { "Drift:Cron", o => o.Drift.Cron = "" },
        { "Drift:MaxDocuments", o => o.Drift.MaxDocuments = 0 },
        { "Analytics:RetentionDays", o => o.Analytics.RetentionDays = 0 },
        { "Analytics:MaxQueryLength", o => o.Analytics.MaxQueryLength = 1 },
        { "Analytics:FlushSeconds", o => o.Analytics.FlushSeconds = 0 },
        { "Analytics:MaxPendingTerms", o => o.Analytics.MaxPendingTerms = 10 },
        { "Analytics:PurgeCron", o => o.Analytics.PurgeCron = "daily" },
        { "Meilisearch:TimeoutSeconds", o => o.Meilisearch.TimeoutSeconds = 0 },
        { "Meilisearch:TaskTimeoutSeconds", o => o.Meilisearch.TaskTimeoutSeconds = 0 }
    };

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void Out_of_range_values_name_their_key(string key, Action<SearchOptions> change) =>
        Failures(change).Should().ContainSingle().Which.Should().Contain($"Search:{key}");
}
