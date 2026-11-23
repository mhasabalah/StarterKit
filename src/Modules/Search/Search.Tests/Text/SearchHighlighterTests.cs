using Search.Contracts.Querying;
using Search.Engine.Text;

namespace Search.Tests.Text;

public sealed class SearchHighlighterTests
{
    private static string? Highlight(string original, string query, SearchMode mode = SearchMode.Full, int max = 160) =>
        SearchHighlighter.Highlight(original, SearchQueryParser.Parse(query, mode), max);

    [Fact]
    public void Marks_every_matching_word_of_a_short_text() =>
        Highlight("Gaming Laptop with a laptop bag", "laptop")
            .Should().Be("Gaming <mark>Laptop</mark> with a <mark>laptop</mark> bag");

    [Fact]
    public void Marks_the_original_text_even_when_the_match_was_normalized()
    {
        Highlight("Café Crème", "cafe").Should().Be("<mark>Café</mark> Crème");
        Highlight("اسمه مُحَمَّدٌ", "محمد").Should().Be("اسمه <mark>مُحَمَّدٌ</mark>");
        Highlight("مدرسة أحمد", "احمد مدرسه").Should().Be("<mark>مدرسة</mark> <mark>أحمد</mark>");
    }

    [Theory]
    [InlineData("Laptops for work", "laptop", "<mark>Laptops</mark> for work")]
    [InlineData("A laptop for work", "laptops", "A <mark>laptop</mark> for work")]
    [InlineData("A laptop for work", "lap", "A <mark>laptop</mark> for work")]
    [InlineData("قرأت الكتاب", "كتاب", "قرأت <mark>الكتاب</mark>")]
    [InlineData("وصل بالكتاب", "كتاب", "وصل <mark>بالكتاب</mark>")]
    public void Matches_word_forms_prefixes_and_arabic_articles(string original, string query, string expected) =>
        Highlight(original, query).Should().Be(expected);

    [Fact]
    public void A_one_letter_query_word_matches_only_itself() =>
        Highlight("a apple and a pear", "a").Should().Be("<mark>a</mark> apple and <mark>a</mark> pear");

    [Fact]
    public void Excluded_words_are_not_highlighted() =>
        Highlight("new laptop, used bag", "laptop -used").Should().Be("new <mark>laptop</mark>, used bag");

    [Fact]
    public void No_match_gives_no_snippet()
    {
        Highlight("A desk lamp", "laptop").Should().BeNull();
        SearchHighlighter.Highlight("A desk lamp", ParsedSearchQuery.Empty).Should().BeNull();
        SearchHighlighter.Highlight(null, SearchQueryParser.Parse("lamp")).Should().BeNull();
    }

    [Fact]
    public void Text_around_the_marks_is_html_encoded() =>
        Highlight("<b>Laptop</b> & \"bag\" 'x'", "laptop")
            .Should().Be("&lt;b&gt;<mark>Laptop</mark>&lt;/b&gt; &amp; &quot;bag&quot; &#39;x&#39;");

    [Fact]
    public void Words_separated_by_punctuation_get_their_own_marks() =>
        Highlight("laptop-laptop", "laptop").Should().Be("<mark>laptop</mark>-<mark>laptop</mark>");

    [Fact]
    public void A_long_text_is_cut_to_a_window_around_the_first_match_on_word_boundaries()
    {
        var text = string.Join(' ', Enumerable.Repeat("filler", 40)) + " the laptop is here " +
                   string.Join(' ', Enumerable.Repeat("tail", 40));

        var snippet = Highlight(text, "laptop", max: 60)!;

        snippet.Should().StartWith(SearchHighlighter.Ellipsis).And.EndWith(SearchHighlighter.Ellipsis);
        snippet.Should().Contain("<mark>laptop</mark>");
        var plain = snippet.Replace(SearchHighlighter.MarkOpen, "").Replace(SearchHighlighter.MarkClose, "")
            .Trim(SearchHighlighter.Ellipsis[0]);
        plain.Length.Should().BeLessThanOrEqualTo(60);
        plain.Should().NotStartWith(" ").And.NotEndWith(" ");
        plain.Split(' ').Should().OnlyContain(w => w == "filler" || w == "the" || w == "laptop" || w == "is" || w == "here" || w == "tail");
    }

    [Fact]
    public void A_match_at_the_start_of_a_long_text_needs_no_leading_ellipsis()
    {
        var snippet = Highlight("laptop " + new string('x', 300), "laptop", max: 40)!;

        snippet.Should().StartWith("<mark>laptop</mark>").And.EndWith(SearchHighlighter.Ellipsis);
    }

    [Fact]
    public void A_window_never_cuts_a_surrogate_pair()
    {
        var rockets = string.Concat(Enumerable.Repeat("\U0001F680", 100));
        var text = rockets + " laptop " + rockets;

        var snippet = Highlight(text, "laptop", max: 41)!;

        snippet.Should().Contain("<mark>laptop</mark>");
        snippet.Where(char.IsHighSurrogate).Count().Should().Be(snippet.Count(char.IsLowSurrogate));
        foreach (var (c, i) in snippet.Select((c, i) => (c, i)).Where(x => char.IsLowSurrogate(x.c)))
            char.IsHighSurrogate(snippet[i - 1]).Should().BeTrue();
    }

    [Fact]
    public void Matches_are_reported_as_original_ranges()
    {
        const string text = "A مُحَمَّدٌ laptop";

        SearchHighlighter.FindMatches(text, SearchQueryParser.Parse("محمد laptop")).Select(s => text.Substring(s.Start, s.Length))
            .Should().Equal("مُحَمَّدٌ", "laptop");
    }

    [Fact]
    public void A_too_small_window_is_rejected() =>
        FluentActions.Invoking(() => SearchHighlighter.Highlight("x", SearchQueryParser.Parse("x"), 5))
            .Should().Throw<ArgumentOutOfRangeException>();
}
