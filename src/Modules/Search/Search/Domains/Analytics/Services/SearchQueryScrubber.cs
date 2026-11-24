using System.Text.RegularExpressions;
using Search.Engine.Text;

namespace Search.Domains.Analytics.Services;

/// <summary>
/// Turns a search text into a term that may be kept for analytics, or <c>null</c> when it must not be. The term is
/// normalized like the index (case, accents, Arabic letter forms, white space), so variants of one search count
/// together. Searches people type to find a person or an account are dropped rather than masked:
/// <list type="bullet">
/// <item>anything with <c>@</c> (email addresses, handles);</item>
/// <item>six or more digits, also with spaces, dashes or dots between them (phone, card, national id, order numbers);</item>
/// <item>terms shorter than <see cref="MinLength"/> characters (noise).</item>
/// </list>
/// Longer terms are cut to the configured length. Nothing about the user who searched is ever recorded.
/// </summary>
public static partial class SearchQueryScrubber
{
    public const int MinLength = 2;

    public static string? Scrub(string? text, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, MinLength);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var term = SearchTextNormalizer.Normalize(text).Value;
        if (term.Contains('@', StringComparison.Ordinal) || LooksLikeANumber(term))
            return null;

        if (term.Length > maxLength)
        {
            var cut = char.IsHighSurrogate(term[maxLength - 1]) ? maxLength - 1 : maxLength; // never split a pair
            term = term[..cut].TrimEnd();
        }

        return term.Length < MinLength ? null : term;
    }

    /// <summary>A match that times out counts as a number: when in doubt, the term is not kept (and the search never fails).</summary>
    private static bool LooksLikeANumber(string term)
    {
        try
        {
            return LongNumber().IsMatch(term);
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    [GeneratedRegex(@"\d(?:[\s.\-]?\d){5,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex LongNumber();
}
