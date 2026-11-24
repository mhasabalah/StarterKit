using Shared.Core.Data;

namespace Shared.Core.Tests.Data;

public sealed class LikePatternTests
{
    [Theory]
    [InlineData("50%", "%50\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("back\\slash", "%back\\\\slash%")]
    [InlineData("  plain  ", "%plain%")]
    public void Contains_EscapesWildcards_AndTrims(string input, string expected) =>
        LikePattern.Contains(input).Should().Be(expected);

    [Theory]
    [InlineData("card_visa", "card\\_visa")]
    [InlineData(" 100% ", " 100\\% ")]
    public void Escape_EscapesWithoutTrimmingOrWrapping(string input, string expected) =>
        LikePattern.Escape(input).Should().Be(expected);
}
