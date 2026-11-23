using Search.Contracts.Querying;
using Search.Domains.Querying.Features.SearchDocuments;
using Search.Engine;
using Search.Options;

namespace Search.Tests.Querying;

public sealed class SearchDocumentsQueryValidatorTests
{
    private static readonly SearchDocumentsQueryValidator Validator = new(
        new SearchIndexRegistry([TestIndexes.Articles()]),
        Microsoft.Extensions.Options.Options.Create(new SearchOptions()),
        TestSupport.Localizer());

    private static SearchDocumentsQuery Query(Func<SearchDocumentsQuery, SearchDocumentsQuery>? change = null)
    {
        var query = new SearchDocumentsQuery { IndexKey = "test.articles", Text = "laptop" };
        return change is null ? query : change(query);
    }

    private static List<string> Errors(SearchDocumentsQuery query) =>
        Validator.Validate(query).Errors.Select(e => e.ErrorMessage).ToList();

    [Fact]
    public void A_query_that_fits_the_index_is_valid() =>
        Errors(Query(q => q with
        {
            Mode = SearchMode.Autocomplete,
            Filters =
            [
                new KeywordFilter("status", "Published"),
                new KeywordsFilter("tag_ids", ["a", "b"], SearchMatch.All),
                new KeywordsFilter("status", ["Draft", "Published"]),
                new NumberRangeFilter("price", 10, 20),
                new DateRangeFilter("published_at", DateTime.UtcNow.AddDays(-7), null),
                new BoolFilter("featured", true)
            ],
            Facets = ["status", "tag_ids", "featured"],
            Sort = new SearchSort("price", Descending: true),
            Page = 2,
            PageSize = 50,
            Highlight = true
        })).Should().BeEmpty();

    [Fact]
    public void Browsing_without_text_is_valid() =>
        Errors(Query(q => q with { Text = null })).Should().BeEmpty();

    [Fact]
    public void An_unknown_index_is_reported_and_field_rules_are_skipped() =>
        Errors(Query(q => q with { IndexKey = "test.missing", Facets = ["anything"] }))
            .Should().Equal("There is no search index 'test.missing'.");

    [Fact]
    public void Text_longer_than_the_limit_is_rejected() =>
        Errors(Query(q => q with { Text = new string('a', 201) }))
            .Should().Equal("The search text must not exceed 200 characters.");

    [Theory]
    [InlineData(0, 20, "The page must be at least 1.")]
    [InlineData(1, 0, "The page size must be between 1 and 100.")]
    [InlineData(1, 101, "The page size must be between 1 and 100.")]
    [InlineData(51, 20, "Results can be paged only within the first 1000 matches.")]
    public void Paging_stays_within_the_limits(int page, int pageSize, string error) =>
        Errors(Query(q => q with { Page = page, PageSize = pageSize })).Should().Equal(error);

    [Fact]
    public void An_undefined_mode_is_rejected() =>
        Errors(Query(q => q with { Mode = (SearchMode)7 })).Should().Equal("The search mode is not valid.");

    public static TheoryData<SearchFilter, string> BadFilters => new()
    {
        { new KeywordFilter("colour", "red"), "'colour' is not a filter field of this index." },
        { new KeywordFilter("price", "10"), "The filter on 'price' does not fit the type of the field." },
        { new KeywordsFilter("status", ["a"], SearchMatch.All), "The filter on 'status' does not fit the type of the field." },
        { new KeywordsFilter("tag_ids", []), "The filter on 'tag_ids' needs 1 to 100 values." },
        { new KeywordsFilter("tag_ids", [.. Enumerable.Range(0, 101).Select(i => $"t{i}")]), "The filter on 'tag_ids' needs 1 to 100 values." },
        { new NumberRangeFilter("status", 1, 2), "The filter on 'status' does not fit the type of the field." },
        { new NumberRangeFilter("price", 20, 10), "The filter on 'price' needs a lower bound that is not above its upper bound." },
        { new DateRangeFilter("price", DateTime.UtcNow, null), "The filter on 'price' does not fit the type of the field." },
        { new DateRangeFilter("published_at", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), null), "The date filter on 'published_at' must use UTC." },
        { new DateRangeFilter("published_at", DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)), "The filter on 'published_at' needs a lower bound that is not above its upper bound." },
        { new BoolFilter("status", true), "The filter on 'status' does not fit the type of the field." }
    };

    [Theory]
    [MemberData(nameof(BadFilters))]
    public void A_filter_that_does_not_fit_its_field_is_rejected(SearchFilter filter, string error) =>
        Errors(Query(q => q with { Filters = [filter] })).Should().Equal(error);

    [Fact]
    public void Only_facetable_fields_can_be_counted() =>
        Errors(Query(q => q with { Facets = ["price", "missing"] })).Should().Equal(
            "Values of 'price' cannot be counted.", "Values of 'missing' cannot be counted.");

    [Fact]
    public void Too_many_facets_are_rejected() =>
        Errors(Query(q => q with { Facets = [.. Enumerable.Repeat("status", 11)] }))
            .Should().Equal("At most 10 facets can be requested.");

    [Fact]
    public void Too_many_filters_are_rejected() =>
        Errors(Query(q => q with { Filters = [.. Enumerable.Repeat(new BoolFilter("featured", true), 33)] }))
            .Should().Equal("At most 32 filters can be used.");

    [Theory]
    [InlineData("status")]
    [InlineData("tag_ids")]
    [InlineData("missing")]
    public void Only_sortable_fields_can_order_the_results(string field) =>
        Errors(Query(q => q with { Sort = new SearchSort(field) })).Should().Equal($"Results cannot be sorted by '{field}'.");

    [Fact]
    public void The_messages_are_localized_in_arabic()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ar");
            Errors(Query(q => q with { Sort = new SearchSort("status") })).Should().Equal("لا يمكن ترتيب النتائج حسب 'status'.");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
    }
}

public sealed class SearchDocumentsHandlerTests
{
    [Fact]
    public async Task Without_an_engine_every_index_reports_not_ready()
    {
        var result = await new SearchDocumentsHandler().Handle(
            new SearchDocumentsQuery { IndexKey = "test.articles", Text = "laptop", Page = 2, PageSize = 10 }, CancellationToken.None);

        result.Status.Should().Be(SearchStatus.IndexNotReady);
        result.Hits.Should().BeEmpty();
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
    }
}
