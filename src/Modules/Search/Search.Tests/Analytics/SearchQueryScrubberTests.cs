using Search.Domains.Analytics.Services;

namespace Search.Tests.Analytics;

public sealed class SearchQueryScrubberTests
{
    [Theory]
    [InlineData("  Wireless   MOUSE ", "wireless mouse")]
    [InlineData("Café crème", "cafe creme")]
    [InlineData("أحمد", "احمد")]
    [InlineData("order 12345", "order 12345")]         // five digits: kept (sizes, years, models)
    [InlineData("iphone 15 pro 256", "iphone 15 pro 256")]
    public void A_term_is_normalized_like_the_index(string text, string expected) =>
        SearchQueryScrubber.Scrub(text, 64).Should().Be(expected);

    [Theory]
    [InlineData("someone@example.com")]
    [InlineData("contact @handle")]
    [InlineData("123456")]
    [InlineData("+20 100 123 4567")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("id 29.801.011.234")]
    [InlineData("رقم ١٢٣٤٥٦٧")]                          // Arabic-Indic digits are folded first
    public void A_term_that_could_identify_a_person_or_account_is_dropped(string text) =>
        SearchQueryScrubber.Scrub(text, 64).Should().BeNull();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x")]
    [InlineData("‏")]
    public void Empty_and_one_character_terms_are_dropped(string? text) =>
        SearchQueryScrubber.Scrub(text, 64).Should().BeNull();

    [Fact]
    public void A_long_term_is_cut_without_a_trailing_space() =>
        SearchQueryScrubber.Scrub("abcde fghij", 6).Should().Be("abcde");

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var scrubbed = SearchQueryScrubber.Scrub("ab\U0001F680cd", 3);

        scrubbed.Should().Be("ab");
        scrubbed!.Should().NotContain("\uD83D");
    }
}
