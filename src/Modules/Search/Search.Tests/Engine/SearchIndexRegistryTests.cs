using Search.Contracts.Indexing;
using Search.Contracts.Querying;
using Search.Engine;
using Shared.Contracts.Localization;

namespace Search.Tests.Engine;

public sealed class SearchIndexRegistryTests
{
    [Fact]
    public void Valid_definitions_are_registered_by_key()
    {
        var registry = new SearchIndexRegistry([TestIndexes.Articles(), TestIndexes.Articles("test.notes")]);

        registry.Indexes.Select(i => i.Key).Should().BeEquivalentTo(["test.articles", "test.notes"]);
        registry.Find("test.articles")!.TextFields.Should().ContainKeys("title", "body");
        registry.Find("test.articles")!.FilterFields.Should().ContainKeys("status", "tag_ids", "price", "published_at", "featured");
        registry.Find("missing").Should().BeNull();
    }

    [Fact]
    public void No_definitions_is_a_valid_empty_registry() =>
        new SearchIndexRegistry([]).Indexes.Should().BeEmpty();

    [Fact]
    public void A_duplicate_key_stops_the_host() =>
        FluentActions.Invoking(() => new SearchIndexRegistry([TestIndexes.Articles(), TestIndexes.Articles()]))
            .Should().Throw<InvalidOperationException>().WithMessage("*Two search indexes use the key 'test.articles'*");

    [Theory]
    [InlineData("articles")]
    [InlineData("Test.articles")]
    [InlineData("test.")]
    [InlineData("1test.articles")]
    [InlineData("test.Articles")]
    [InlineData("test articles")]
    public void A_key_that_is_not_module_dot_entity_is_rejected(string key) =>
        Invalid(TestIndexes.Articles() with { Key = key }).Should().Contain("key must look like 'module.entity'");

    [Fact]
    public void A_key_longer_than_the_limit_is_rejected() =>
        Invalid(TestIndexes.Articles() with { Key = "test." + new string('a', SearchIndexDefinition.KeyMaxLength) })
            .Should().Contain("key must look like");

    [Fact]
    public void The_name_needs_both_languages() =>
        Invalid(TestIndexes.Articles() with { Name = LocalizedText.Create("Articles", " ") })
            .Should().Contain("English and Arabic");

    [Fact]
    public void At_least_one_text_field_is_required() =>
        Invalid(TestIndexes.Articles() with { TextFields = [] }).Should().Contain("at least one text field");

    [Theory]
    [InlineData("Title")]
    [InlineData("_title")]
    [InlineData("1title")]
    [InlineData("ti-tle")]
    [InlineData("title.en")]
    public void A_field_name_that_is_not_lower_snake_case_is_rejected(string name) =>
        Invalid(TestIndexes.Articles() with
        {
            TextFields = [new SearchTextField(name, SearchWeight.High, SearchAnalyzer.Simple)]
        })
            .Should().Contain("lower snake case");

    [Fact]
    public void A_field_name_used_by_a_text_and_a_filter_field_is_rejected() =>
        Invalid(TestIndexes.Articles() with
        {
            FilterFields = [new SearchFilterField("title", SearchFieldType.Keyword)]
        })
            .Should().Contain("'title' is used twice");

    [Theory]
    [InlineData(SearchFieldType.Number)]
    [InlineData(SearchFieldType.Date)]
    public void Only_keyword_and_boolean_fields_can_be_facetable(SearchFieldType type) =>
        Invalid(TestIndexes.Articles() with { FilterFields = [new SearchFilterField("value", type) { Facetable = true }] })
            .Should().Contain("cannot be facetable");

    [Fact]
    public void A_keyword_list_cannot_be_sortable() =>
        Invalid(TestIndexes.Articles() with
        {
            FilterFields = [new SearchFilterField("tag_ids", SearchFieldType.KeywordList) { Sortable = true }],
            DefaultSort = null
        })
            .Should().Contain("cannot be sortable");

    [Fact]
    public void The_default_sort_must_be_a_sortable_field() =>
        Invalid(TestIndexes.Articles() with { DefaultSort = new SearchSort("status") })
            .Should().Contain("default sort field 'status' must be a sortable filter field");

    [Theory]
    [InlineData(0)]
    [InlineData(100_001)]
    public void A_text_field_needs_a_sane_max_length(int maxLength) =>
        Invalid(TestIndexes.Articles() with
        {
            TextFields = [new SearchTextField("title", SearchWeight.High, SearchAnalyzer.Simple) { MaxLength = maxLength }]
        })
            .Should().Contain("MaxLength");

    [Fact]
    public void Unknown_enum_values_are_rejected() =>
        Invalid(TestIndexes.Articles() with
        {
            TextFields = [new SearchTextField("title", (SearchWeight)42, (SearchAnalyzer)42)],
            FilterFields = [new SearchFilterField("status", (SearchFieldType)42)],
            DefaultSort = null
        })
            .Should().Contain("unknown weight").And.Contain("unknown analyzer").And.Contain("unknown type");

    [Fact]
    public void Every_problem_of_every_definition_is_reported_at_once()
    {
        var message = FluentActions.Invoking(() => new SearchIndexRegistry(
            [
                TestIndexes.Articles() with { TextFields = [] },
                TestIndexes.Articles("BAD") with { DefaultSort = new SearchSort("missing") }
            ]))
            .Should().Throw<InvalidOperationException>().Which.Message;

        message.Should().Contain("'test.articles': at least one text field")
            .And.Contain("'BAD': the key must look like")
            .And.Contain("'BAD': the default sort field 'missing'");
    }

    private static string Invalid(SearchIndexDefinition definition) =>
        FluentActions.Invoking(() => new SearchIndexRegistry([definition]))
            .Should().Throw<InvalidOperationException>().Which.Message;
}
