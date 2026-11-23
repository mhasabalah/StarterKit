using System.Globalization;
using System.Text;

namespace Search.Engine.Text;

/// <summary>
/// Text in its canonical search form, with a map back to the original: <c>Map[i]</c> is the index in
/// <see cref="Original"/> of the character that produced <c>Value[i]</c>.
/// </summary>
public sealed class NormalizedText
{
    internal NormalizedText(string original, string value, int[] map)
    {
        Original = original;
        Value = value;
        Map = map;
    }

    public string Original { get; }

    public string Value { get; }

    public IReadOnlyList<int> Map { get; }

    public static readonly NormalizedText Empty = new(string.Empty, string.Empty, []);

    /// <summary>
    /// The range of <see cref="Original"/> that produced <c>Value[start..start+length]</c>, widened to whole original
    /// characters (a surrogate pair, or a ligature that expanded to several characters, is never cut).
    /// </summary>
    public (int Start, int Length) ToOriginal(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + length, Value.Length);

        var first = Map[start];
        var last = Map[start + length - 1];
        var end = last + (char.IsHighSurrogate(Original[last]) && last + 1 < Original.Length ? 2 : 1);

        // Characters the normalizer dropped between the last mapped one and the next (diacritics, tatweel) belong to it.
        var next = start + length < Value.Length ? Map[start + length] : Original.Length;
        while (end < next && SearchTextNormalizer.IsDroppedMark(Original[end]))
            end++;

        return (first, end - first);
    }
}

/// <summary>
/// Reduces text to its canonical search form, the same way for documents and queries, so every engine matches
/// "أحمد" with "احمد" and "Café" with "cafe". The Arabic rules follow Lucene's <c>ArabicNormalizer</c>:
/// <list type="bullet">
/// <item>diacritics (tashkeel, superscript alef, Quranic marks) and tatweel are removed;</item>
/// <item>أ إ آ ٱ become ا, ى becomes ي, ة becomes ه; Persian ک and ی become ك and ي;</item>
/// <item>Arabic-Indic and Persian digits become 0-9;</item>
/// <item>compatibility forms are decomposed and their marks dropped: presentation-form ligatures (ﻻ becomes لا, ﬁ
/// becomes fi), and, beyond Lucene, hamza on waw and on ya (ؤ becomes و, ئ becomes ي), so a word written without
/// its hamza still matches;</item>
/// <item>Latin (and other scripts) lose their diacritics and are lower-cased;</item>
/// <item>format and control characters are removed; any run of white space becomes one space; the ends are trimmed.</item>
/// </list>
/// Changing a rule changes the stored text: bump <see cref="RegisteredIndex.FormatVersion"/>, which rebuilds every
/// index.
/// </summary>
public static class SearchTextNormalizer
{
    public static NormalizedText Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return NormalizedText.Empty;

        var value = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        var pendingSpace = false;

        void Emit(char c, int origin)
        {
            if (pendingSpace && value.Length > 0)
            {
                value.Append(' ');
                map.Add(origin);
            }

            pendingSpace = false;
            value.Append(c);
            map.Add(origin);
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = true;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                // Outside the BMP (emoji, rare scripts): kept as is, never split.
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    Emit(c, i);
                    value.Append(text[i + 1]);
                    map.Add(i);
                    i++;
                }

                continue; // a lone surrogate is dropped
            }

            if (IsDroppedMark(c) || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.Control)
                continue;

            if (MapArabic(c) is { } arabic)
            {
                Emit(arabic, i);
                continue;
            }

            if (c < 0x80)
            {
                Emit(char.ToLowerInvariant(c), i);
                continue;
            }

            // Compatibility decomposition: drop the combining marks, keep (and lower-case) the rest.
            foreach (var d in c.ToString().Normalize(NormalizationForm.FormKD))
            {
                if (char.GetUnicodeCategory(d) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark)
                    continue;
                if (char.IsWhiteSpace(d))
                {
                    pendingSpace = true;
                    continue;
                }

                Emit(MapArabic(d) ?? char.ToLowerInvariant(d), i);
            }
        }

        return new NormalizedText(text, value.ToString(), [.. map]);
    }

    /// <summary>Arabic diacritics, Quranic annotation marks and tatweel: removed without a trace.</summary>
    internal static bool IsDroppedMark(char c) =>
        c is >= 'ً' and <= 'ٟ' or 'ٰ' or >= 'ۖ' and <= 'ۭ' or 'ـ';

    private static char? MapArabic(char c) => c switch
    {
        'آ' or 'أ' or 'إ' or 'ٱ' => 'ا', // آ أ إ ٱ -> ا
        'ى' => 'ي',                                     // ى -> ي
        'ة' => 'ه',                                     // ة -> ه
        'ک' => 'ك',                                     // ک -> ك
        'ی' => 'ي',                                     // ی -> ي
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
        _ => null
    };
}
