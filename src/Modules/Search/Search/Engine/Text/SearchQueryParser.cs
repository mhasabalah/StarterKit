using System.Text;

namespace Search.Engine.Text;

/// <summary>One searched word, or a phrase of adjacent words. Words are normalized (<see cref="SearchTextNormalizer"/>).</summary>
/// <param name="Words">One word, or the words of a phrase in order.</param>
/// <param name="Negated">The document must not contain it (<c>-word</c>).</param>
/// <param name="Prefix">The (single) word also matches as a prefix: the last word typed in autocomplete.</param>
public sealed record SearchTerm(IReadOnlyList<string> Words, bool Negated = false, bool Prefix = false)
{
    public bool IsPhrase => Words.Count > 1;

    public override string ToString()
    {
        var text = IsPhrase ? $"\"{string.Join(' ', Words)}\"" : Words[0] + (Prefix ? "*" : "");
        return Negated ? "-" + text : text;
    }
}

/// <summary>A condition of the query: one of the alternatives must match (<c>a OR b</c>); a single term otherwise.</summary>
public sealed record SearchClause(IReadOnlyList<SearchTerm> Alternatives)
{
    public bool Negated => Alternatives is [{ Negated: true }];

    public override string ToString() => string.Join(" or ", Alternatives);
}

/// <summary>
/// The engine-neutral form of a search text: every clause must match. Engines translate it to their own syntax, so
/// the grammar is the same on every engine.
/// </summary>
public sealed class ParsedSearchQuery
{
    private ParsedSearchQuery(IReadOnlyList<SearchClause> clauses)
    {
        Clauses = clauses;
        PositiveWords = clauses.Where(c => !c.Negated)
            .SelectMany(c => c.Alternatives)
            .SelectMany(t => t.Words)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static readonly ParsedSearchQuery Empty = new([]);

    public IReadOnlyList<SearchClause> Clauses { get; }

    /// <summary>The distinct words the documents should contain (for highlighting and the fuzzy fallback).</summary>
    public IReadOnlyList<string> PositiveWords { get; }

    /// <summary>No words to match: the query browses (filters and sort only).</summary>
    public bool IsEmpty => Clauses.Count == 0;

    /// <summary>The canonical, normalized text of the query (analytics key, logs).</summary>
    public override string ToString() => string.Join(' ', Clauses);

    internal static ParsedSearchQuery From(IReadOnlyList<SearchClause> clauses) =>
        clauses.Any(c => !c.Negated) ? new ParsedSearchQuery(clauses) : Empty;
}

/// <summary>
/// Parses the search text people type, the same grammar as web search engines: words (all must match),
/// <c>"quoted phrases"</c>, <c>-excluded</c> words or phrases, and <c>OR</c> between two alternatives. Text is
/// normalized first. A query of excluded words only is treated as no query (it would match nearly everything).
/// </summary>
public static class SearchQueryParser
{
    public static ParsedSearchQuery Parse(string? text, SearchMode mode = SearchMode.Full, int maxTerms = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTerms);
        if (string.IsNullOrWhiteSpace(text))
            return ParsedSearchQuery.Empty;

        var tokens = Tokenize(SearchTextNormalizer.Normalize(text).Value);

        // Autocomplete: the last word is still being typed unless the text ends with a space or a closing quote.
        var typingLastWord = mode == SearchMode.Autocomplete && !char.IsWhiteSpace(text[^1]) && text[^1] != '"';

        var clauses = new List<List<SearchTerm>>();
        var words = 0;
        var orPending = false;
        var lastTermFromLastToken = false;

        for (var t = 0; t < tokens.Count; t++)
        {
            var token = tokens[t];
            lastTermFromLastToken = false;
            if (token.IsOr)
            {
                orPending = clauses.Count > 0 && !clauses[^1][0].Negated;
                continue;
            }

            var room = maxTerms - words;
            if (room <= 0)
                break;

            var term = new SearchTerm(token.Words.Take(room).ToList(), token.Negated);
            words += term.Words.Count;

            if (orPending && !term.Negated)
                clauses[^1].Add(term);
            else
                clauses.Add([term]);
            orPending = false;
            lastTermFromLastToken = t == tokens.Count - 1 && !token.Quoted && term.Words.Count == token.Words.Count;
        }

        if (typingLastWord && lastTermFromLastToken && clauses[^1][^1] is { Negated: false, IsPhrase: false } last)
            clauses[^1][^1] = last with { Prefix = true };

        return ParsedSearchQuery.From(clauses.Select(c => new SearchClause(c)).ToList());
    }

    /// <summary>The words of normalized text: runs of letters and digits (punctuation separates them).</summary>
    public static IReadOnlyList<(int Start, int Length)> FindWords(string normalized)
    {
        var words = new List<(int, int)>();
        var start = -1;
        var i = 0;
        while (i < normalized.Length)
        {
            var rune = Rune.GetRuneAt(normalized, i);
            if (Rune.IsLetterOrDigit(rune))
            {
                if (start < 0)
                    start = i;
            }
            else if (start >= 0)
            {
                words.Add((start, i - start));
                start = -1;
            }

            i += rune.Utf16SequenceLength;
        }

        if (start >= 0)
            words.Add((start, normalized.Length - start));
        return words;
    }

    private sealed record Token(IReadOnlyList<string> Words, bool Negated, bool Quoted, bool IsOr = false);

    private static List<Token> Tokenize(string normalized)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < normalized.Length)
        {
            if (normalized[i] == ' ')
            {
                i++;
                continue;
            }

            var negated = false;
            if (normalized[i] == '-' && i + 1 < normalized.Length && normalized[i + 1] != ' ' && normalized[i + 1] != '-')
            {
                negated = true;
                i++;
            }

            string chunk;
            var quoted = normalized[i] == '"';
            if (quoted)
            {
                var close = normalized.IndexOf('"', i + 1);
                var end = close < 0 ? normalized.Length : close;
                chunk = normalized[(i + 1)..end];
                i = close < 0 ? normalized.Length : close + 1;
            }
            else
            {
                var end = i;
                while (end < normalized.Length && normalized[end] != ' ' && normalized[end] != '"')
                    end++;
                chunk = normalized[i..end];
                i = end;
            }

            var words = FindWords(chunk).Select(w => chunk.Substring(w.Start, w.Length)).ToList();
            if (words.Count == 0)
                continue;

            if (!quoted && !negated && words is ["or"] && chunk == "or")
                tokens.Add(new Token(words, Negated: false, Quoted: false, IsOr: true));
            else
                tokens.Add(new Token(words, negated, quoted));
        }

        return tokens;
    }
}
