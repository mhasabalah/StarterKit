using Search.Contracts.Indexing;
using Search.Contracts.Querying;
using Search.Engine;
using Shared.Contracts.Localization;

namespace Search.Tests.Engine;

public sealed class RegisteredIndexTests
{
    private static RegisteredIndex Register(SearchIndexDefinition definition) =>
        new SearchIndexRegistry([definition]).Find(definition.Key)!;

    [Fact]
    public void A_document_that_matches_the_definition_has_no_problems()
    {
        var document = SearchDocument.For(Guid.NewGuid(), 3)
            .Text("title", "Laptop").Text("body", "A light laptop")
            .Keyword("status", "Published").Keywords("tag_ids", ["a", "b"])
            .Number("price", 999.5m).Date("published_at", DateTime.UtcNow).Bool("featured", true)
            .Build();

        Register(TestIndexes.Articles()).Validate(document).Should().BeEmpty();
    }

    [Fact]
    public void Unknown_fields_and_wrong_types_are_reported()
    {
        var document = SearchDocument.For("a-1", 1)
            .Text("subtitle", "x")
            .Keyword("colour", "red")
            .Keyword("price", "cheap")
            .Build();

        Register(TestIndexes.Articles()).Validate(document).Should().BeEquivalentTo(
        [
            "'subtitle' is not a text field of 'test.articles'.",
            "'colour' is not a filter field of 'test.articles'.",
            "'price' of 'test.articles' takes a Number value, not a Keyword."
        ]);
    }

    [Fact]
    public void The_fingerprint_is_stable_for_the_same_definition() =>
        Register(TestIndexes.Articles()).Fingerprint.Should()
            .Be(Register(TestIndexes.Articles()).Fingerprint)
            .And.MatchRegex("^[0-9a-f]{64}$");

    [Fact]
    public void The_display_name_does_not_change_the_fingerprint() =>
        Register(TestIndexes.Articles() with { Name = LocalizedText.Create("Posts", "منشورات") }).Fingerprint
            .Should().Be(Register(TestIndexes.Articles()).Fingerprint);

    public static TheoryData<string, Func<SearchIndexDefinition, SearchIndexDefinition>> Changes => new()
    {
        { "key", d => d with { Key = "test.other" } },
        { "weight", d => d with { TextFields = [d.TextFields[0] with { Weight = SearchWeight.Low }, d.TextFields[1]] } },
        { "analyzer", d => d with { TextFields = [d.TextFields[0] with { Analyzer = SearchAnalyzer.Simple }, d.TextFields[1]] } },
        { "suggest", d => d with { TextFields = [d.TextFields[0] with { Suggest = false }, d.TextFields[1]] } },
        { "highlight", d => d with { TextFields = [d.TextFields[0] with { Highlight = false }, d.TextFields[1]] } },
        { "max length", d => d with { TextFields = [d.TextFields[0], d.TextFields[1] with { MaxLength = 10 }] } },
        { "added field", d => d with { FilterFields = [.. d.FilterFields, new SearchFilterField("owner_id", SearchFieldType.Keyword)] } },
        { "facetable", d => d with { FilterFields = [d.FilterFields[0] with { Facetable = false }, .. d.FilterFields.Skip(1)] } },
        { "sort", d => d with { DefaultSort = new SearchSort("price") } }
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Anything_that_shapes_the_stored_index_changes_the_fingerprint(
        string change, Func<SearchIndexDefinition, SearchIndexDefinition> apply) =>
        Register(apply(TestIndexes.Articles())).Fingerprint
            .Should().NotBe(Register(TestIndexes.Articles()).Fingerprint, change);
}
