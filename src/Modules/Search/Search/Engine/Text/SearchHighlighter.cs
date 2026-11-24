using System.Text;

namespace Search.Engine.Text;

/// <summary>
/// Builds highlighted snippets of the ORIGINAL text, identically for every engine. A field word is highlighted when
/// its normalized form matches a query word: equal, starting with it (autocomplete, stems: "lap" → "laptop"), the
/// query word starting with it ("laptops" → "laptop"), or the same after an Arabic article or prefix
/// ("كتاب" → "الكتاب", "بالكتاب"). Excluded words are never highlighted.
/// </summary>
public static class SearchHighlighter
{
    public const string MarkOpen = "<mark>";
    public const string MarkClose = "</mark>";
    public const int DefaultMaxLength = 160;
    public const string Ellipsis = "…";
    private const char EllipsisChar = '…';

    // Longest first: "وال" before "ال".
    private static readonly string[] ArabicPrefixes = ["وال", "بال", "كال", "فال", "لل", "ال"];

    /// <summary>
    /// A snippet of <paramref name="original"/> of about <paramref name="maxLength"/> characters around the first
    /// match, HTML-encoded, with the matched words in <see cref="MarkOpen"/>/<see cref="MarkClose"/>; <c>null</c>
    /// when nothing matches.
    /// </summary>
    public static string? Highlight(string? original, ParsedSearchQuery query, int maxLength = DefaultMaxLength)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 20);
        if (string.IsNullOrEmpty(original) || query.IsEmpty)
            return null;

        var spans = FindMatches(original, query);
        if (spans.Count == 0)
            return null;

        var (windowStart, windowEnd) = Window(original, spans[0], maxLength);
        var snippet = new StringBuilder();
        if (windowStart > 0)
            snippet.Append(EllipsisChar);

        var position = windowStart;
        foreach (var (start, length) in spans)
        {
            var end = start + length;
            if (end <= windowStart || start >= windowEnd)
                continue;
            var from = Math.Max(start, windowStart);
            var to = Math.Min(end, windowEnd);

            AppendEncoded(snippet, original, position, from);
            snippet.Append(MarkOpen);
            AppendEncoded(snippet, original, from, to);
            snippet.Append(MarkClose);
            position = to;
        }

        AppendEncoded(snippet, original, position, windowEnd);
        if (windowEnd < original.Length)
            snippet.Append(EllipsisChar);
        return snippet.ToString();
    }

    /// <summary>The ranges of <paramref name="original"/> whose words match the query, in order, merged when they touch.</summary>
    public static IReadOnlyList<(int Start, int Length)> FindMatches(string original, ParsedSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(query);

        var normalized = SearchTextNormalizer.Normalize(original);
        var spans = new List<(int Start, int Length)>();

        foreach (var (start, length) in SearchQueryParser.FindWords(normalized.Value))
        {
            var word = normalized.Value.Substring(start, length);
            if (!query.PositiveWords.Any(q => Matches(word, q)))
                continue;

            var span = normalized.ToOriginal(start, length);
            if (spans.Count > 0 && spans[^1].Start + spans[^1].Length >= span.Start)
            {
                var merged = spans[^1];
                spans[^1] = (merged.Start, Math.Max(merged.Start + merged.Length, span.Start + span.Length) - merged.Start);
            }
            else
            {
                spans.Add(span);
            }
        }

        return spans;
    }

    internal static bool Matches(string word, string queryWord)
    {
        if (Compare(word, queryWord))
            return true;

        foreach (var prefix in ArabicPrefixes)
        {
            if (word.Length > prefix.Length + 1 && word.StartsWith(prefix, StringComparison.Ordinal))
                return Compare(word[prefix.Length..], queryWord);
        }

        return false;
    }

    private static bool Compare(string word, string queryWord) =>
        word == queryWord
        || (queryWord.Length >= 2 && word.StartsWith(queryWord, StringComparison.Ordinal))
        || (word.Length >= 4 && queryWord.StartsWith(word, StringComparison.Ordinal));

    private static (int Start, int End) Window(string text, (int Start, int Length) first, int maxLength)
    {
        if (text.Length <= maxLength)
            return (0, text.Length);

        // Put the first match about a third into the window, then snap both ends to word boundaries.
        var start = Math.Clamp(first.Start - (maxLength / 3), 0, text.Length - maxLength);
        var end = Math.Min(text.Length, start + maxLength);

        start = SnapBack(text, start, first.Start);
        end = SnapForward(text, end, first.Start + first.Length);
        return (start, end);
    }

    private static int SnapBack(string text, int start, int limit)
    {
        if (start == 0)
            return 0;
        for (var i = start; i < limit && i < start + 15; i++)
        {
            if (char.IsWhiteSpace(text[i - 1]))
                return i;
        }

        return char.IsLowSurrogate(text[start]) ? start - 1 : start;
    }

    private static int SnapForward(string text, int end, int limit)
    {
        if (end >= text.Length)
            return text.Length;
        for (var i = end; i > limit && i > end - 15; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                return i;
        }

        return char.IsLowSurrogate(text[end]) ? end + 1 : end;
    }

    private static void AppendEncoded(StringBuilder builder, string text, int from, int to)
    {
        for (var i = from; i < to; i++)
        {
            _ = text[i] switch
            {
                '&' => builder.Append("&amp;"),
                '<' => builder.Append("&lt;"),
                '>' => builder.Append("&gt;"),
                '"' => builder.Append("&quot;"),
                '\'' => builder.Append("&#39;"),
                var c => builder.Append(c)
            };
        }
    }
}
