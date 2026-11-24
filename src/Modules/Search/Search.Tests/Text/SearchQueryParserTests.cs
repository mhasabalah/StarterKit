using Search.Contracts.Querying;
using Search.Engine.Text;

namespace Search.Tests.Text;

public sealed class SearchQueryParserTests
{
    private static string Parse(string? text, SearchMode mode = SearchMode.Full, int maxTerms = 10) =>
        SearchQueryParser.Parse(text, mode, maxTerms).ToString();

    [Theory]
    [InlineData("laptop", "laptop")]
    [InlineData("  Gaming   LAPTOP ", "gaming laptop")]
    [InlineData("\"gaming laptop\" bag", "\"gaming laptop\" bag")]
    [InlineData("laptop -refurbished", "laptop -refurbished")]
    [InlineData("laptop -\"open box\"", "laptop -\"open box\"")]
    [InlineData("laptop or notebook", "laptop or notebook")]
    [InlineData("laptop OR notebook OR netbook bag", "laptop or notebook or netbook bag")]
    [InlineData("AB-123", "\"ab 123\"")]
    [InlineData("لابتوب أسود", "لابتوب اسود")]
    [InlineData("\"unclosed phrase", "\"unclosed phrase\"")]
    [InlineData("pre-owned laptop", "\"pre owned\" laptop")]
    public void Parses_words_phrases_exclusions_and_alternatives(string text, string expected) =>
        Parse(text).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ... ???")]
    [InlineData("\"\"")]
    [InlineData("-refurbished")]
    [InlineData("-a -\"b c\"")]
    [InlineData("or")]
    public void Text_without_a_positive_word_is_an_empty_query(string? text) =>
        SearchQueryParser.Parse(text).IsEmpty.Should().BeTrue();

    [Theory]
    [InlineData("or laptop", "laptop")]
    [InlineData("laptop or", "laptop")]
    [InlineData("laptop or -used", "laptop -used")]
    [InlineData("-used or laptop", "-used laptop")]
    [InlineData("a or or b", "a or b")]
    public void A_dangling_or_is_ignored(string text, string expected) =>
        Parse(text).Should().Be(expected);

    [Theory]
    [InlineData("--x", "x")]
    [InlineData("- x", "x")]
    [InlineData("a - b", "a b")]
    public void A_dash_negates_only_when_glued_to_a_word(string text, string expected) =>
        Parse(text).Should().Be(expected);

    [Fact]
    public void Words_beyond_the_limit_are_dropped_and_a_phrase_is_cut() =>
        Parse("one two \"three four five\" six", maxTerms: 4).Should().Be("one two \"three four\"");

    [Theory]
    [InlineData("gaming lap", "gaming lap*")]
    [InlineData("gaming lap ", "gaming lap")]
    [InlineData("\"gaming lap\"", "\"gaming lap\"")]
    [InlineData("laptop -us", "laptop -us")]
    [InlineData("laptop or note", "laptop or note*")]
    [InlineData("laptop or", "laptop")]
    [InlineData("لابت", "لابت*")]
    public void Autocomplete_matches_the_last_word_being_typed_as_a_prefix(string text, string expected) =>
        Parse(text, SearchMode.Autocomplete).Should().Be(expected);

    [Fact]
    public void Full_mode_never_uses_a_prefix() =>
        Parse("gaming lap").Should().Be("gaming lap");

    [Fact]
    public void A_prefix_is_not_applied_to_a_word_cut_by_the_limit() =>
        Parse("one two three", SearchMode.Autocomplete, maxTerms: 2).Should().Be("one two");

    [Fact]
    public void Positive_words_exclude_negated_ones_and_repeat_once()
    {
        var query = SearchQueryParser.Parse("laptop \"laptop bag\" -used");

        query.PositiveWords.Should().Equal("laptop", "bag");
        query.Clauses.Should().HaveCount(3);
        query.Clauses[2].Negated.Should().BeTrue();
    }

    [Fact]
    public void Clauses_expose_their_alternatives_and_terms()
    {
        var query = SearchQueryParser.Parse("\"red lamp\" or bulb");

        query.Clauses.Should().ContainSingle();
        query.Clauses[0].Alternatives.Should().BeEquivalentTo(
        [
            new SearchTerm(["red", "lamp"]),
            new SearchTerm(["bulb"])
        ], o => o.WithStrictOrdering());
        query.Clauses[0].Alternatives[0].IsPhrase.Should().BeTrue();
    }

    [Fact]
    public void Words_are_runs_of_letters_and_digits() =>
        SearchQueryParser.FindWords("ab-12 🚀 x.y").Should().Equal((0, 2), (3, 2), (9, 1), (11, 1));
}
