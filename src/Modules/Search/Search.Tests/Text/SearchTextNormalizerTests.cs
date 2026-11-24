using Search.Engine.Text;

namespace Search.Tests.Text;

public sealed class SearchTextNormalizerTests
{
    [Theory]
    // Arabic: alef forms, alef maqsura, ta marbuta, Persian letters.
    [InlineData("أحمد", "احمد")]
    [InlineData("إسلام", "اسلام")]
    [InlineData("آمن", "امن")]
    [InlineData("ٱلله", "الله")]
    [InlineData("مستشفى", "مستشفي")]
    [InlineData("مدرسة", "مدرسه")]
    [InlineData("کتاب", "كتاب")]
    [InlineData("یوم", "يوم")]
    // Diacritics, superscript alef, Quranic marks and tatweel disappear.
    [InlineData("مُحَمَّدٌ", "محمد")]
    [InlineData("هٰذا", "هذا")]
    [InlineData("كتـــاب", "كتاب")]
    [InlineData("رَحِيمِۖ", "رحيم")]
    // Hamza on waw / ya folds too (beyond Lucene).
    [InlineData("مسؤول", "مسوول")]
    [InlineData("مسئول", "مسيول")]
    // Digits.
    [InlineData("٠١٢٣٤٥٦٧٨٩", "0123456789")]
    [InlineData("۰۱۲۳۴۵۶۷۸۹", "0123456789")]
    // Presentation forms and compatibility ligatures decompose.
    [InlineData("ﻻ", "لا")]
    [InlineData("ﷲ", "الله")]
    [InlineData("ﬁle", "file")]
    // Latin: case and diacritics.
    [InlineData("Café CRÈME", "cafe creme")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("Ñandú", "nandu")]
    [InlineData("ＡＢＣ１２３", "abc123")]
    // White space and invisible characters.
    [InlineData("  laptop \t\n  bag  ", "laptop bag")]
    [InlineData("zero​width‌­join", "zerowidthjoin")]
    [InlineData("a\u0000b", "ab")]
    // Punctuation stays (engines split on it); codes keep their shape.
    [InlineData("SKU AB-12.5/X", "sku ab-12.5/x")]
    public void Normalizes(string input, string expected) =>
        SearchTextNormalizer.Normalize(input).Value.Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("َـ")]
    public void Nothing_left_is_empty(string? input) =>
        SearchTextNormalizer.Normalize(input).Value.Should().BeEmpty();

    [Fact]
    public void Characters_outside_the_bmp_are_kept_whole_and_lone_surrogates_dropped()
    {
        SearchTextNormalizer.Normalize("go 🚀 now").Value.Should().Be("go 🚀 now");
        SearchTextNormalizer.Normalize("a\uD83Db").Value.Should().Be("ab");
    }

    [Theory]
    [InlineData("مُحَمَّدٌ أحمد")]
    [InlineData("  Café  ﷲ ﻻ 🚀 ١٢ ")]
    [InlineData("كتـــاب الإسلام")]
    public void The_map_is_monotonic_and_points_into_the_original(string input)
    {
        var text = SearchTextNormalizer.Normalize(input);

        text.Map.Should().HaveCount(text.Value.Length);
        text.Map.Should().BeInAscendingOrder();
        text.Map.Should().OnlyContain(i => i >= 0 && i < input.Length);
    }

    [Fact]
    public void Normalizing_twice_changes_nothing()
    {
        const string input = "مُحَمَّدٌ  Café ﷲ ١٢ AB-12";
        var once = SearchTextNormalizer.Normalize(input).Value;

        SearchTextNormalizer.Normalize(once).Value.Should().Be(once);
    }

    [Fact]
    public void A_normalized_range_maps_back_to_the_original_word_with_its_diacritics()
    {
        const string input = "اسم مُحَمَّدٌ هنا";
        var text = SearchTextNormalizer.Normalize(input);
        var start = text.Value.IndexOf("محمد", StringComparison.Ordinal);

        var (origin, length) = text.ToOriginal(start, "محمد".Length);

        input.Substring(origin, length).Should().Be("مُحَمَّدٌ");
    }

    [Fact]
    public void A_range_inside_an_expanded_ligature_maps_to_the_whole_ligature()
    {
        const string input = "قال ﷲ";
        var text = SearchTextNormalizer.Normalize(input);

        var (origin, length) = text.ToOriginal(text.Value.IndexOf("الله", StringComparison.Ordinal), 2);

        input.Substring(origin, length).Should().Be("ﷲ");
    }

    [Fact]
    public void A_range_never_cuts_a_surrogate_pair()
    {
        const string input = "x🚀y";
        var text = SearchTextNormalizer.Normalize(input);

        text.ToOriginal(1, 2).Should().Be((1, 2));
    }

    [Fact]
    public void An_out_of_range_span_is_rejected() =>
        FluentActions.Invoking(() => SearchTextNormalizer.Normalize("abc").ToOriginal(2, 2))
            .Should().Throw<ArgumentOutOfRangeException>();
}
