using Search.Contracts.Indexing;
using Search.Engine;

namespace Search.Tests.Engine;

public sealed class SearchDocumentPreparerTests
{
    private static readonly RegisteredIndex Index = new SearchIndexRegistry([TestIndexes.Articles()]).Find("test.articles")!;

    [Fact]
    public void Normalizes_text_collects_suggest_text_and_keeps_highlight_originals()
    {
        var branch = Guid.NewGuid();
        var document = SearchDocument.For("a1", 4).InBranch(branch)
            .Text("title", "Café أحمد").Text("body", "Running SHOES")
            .Keyword("status", "Published")
            .Build();

        var prepared = SearchDocumentPreparer.Prepare(Index, document);

        prepared.Id.Should().Be("a1");
        prepared.Version.Should().Be(4);
        prepared.BranchId.Should().Be(branch);
        prepared.Text.Should().Equal(new Dictionary<string, string> { ["title"] = "cafe احمد", ["body"] = "running shoes" });
        prepared.SuggestText.Should().Be("cafe احمد", "only the title is a suggest field");
        prepared.HighlightOriginals.Should().Equal(new Dictionary<string, string> { ["title"] = "Café أحمد" });
        prepared.Filters.Should().ContainKey("status");
    }

    [Fact]
    public void Text_is_cut_to_the_fields_max_length_without_splitting_a_surrogate_pair()
    {
        var body = new string('a', 1999) + "\U0001F680tail";
        var prepared = SearchDocumentPreparer.Prepare(Index, SearchDocument.For("a1", 1).Text("body", body).Build());

        prepared.Text["body"].Should().Be(new string('a', 1999));
    }

    [Fact]
    public void Text_that_normalizes_to_nothing_is_left_out() =>
        SearchDocumentPreparer.Prepare(Index, SearchDocument.For("a1", 1).Text("title", "َـ").Build())
            .Text.Should().BeEmpty();

    [Fact]
    public void A_document_that_does_not_fit_the_index_is_rejected_with_every_problem()
    {
        var document = SearchDocument.For("a1", 1).Text("subtitle", "x").Keyword("price", "cheap").Build();

        var error = FluentActions.Invoking(() => SearchDocumentPreparer.Prepare(Index, document))
            .Should().Throw<InvalidSearchDocumentException>().Which;

        error.IndexKey.Should().Be("test.articles");
        error.DocumentId.Should().Be("a1");
        error.Problems.Should().HaveCount(2);
    }
}
