using Search.Engine.Text;

namespace Search.Engine.Meilisearch;

/// <summary>
/// The parsed search text as Meilisearch <c>q</c> strings, so the query grammar is the same on every engine:
/// <list type="bullet">
/// <item>Every word must match (the search runs with <c>matchingStrategy: all</c>).</item>
/// <item>A quoted word or phrase matches exactly: no prefix, no typo. Words are quoted, except the autocomplete prefix
/// and, in the fuzzy pass, words long enough for close spellings. Unquoted words go last, where Meilisearch applies
/// prefix matching.</item>
/// <item>Exclusions become <c>-"word"</c>.</item>
/// <item>Meilisearch has no OR: a query with alternatives expands into one conjunctive query per combination (at most
/// <see cref="MaxCombinations"/>), run together as one federated search.</item>
/// </list>
/// </summary>
internal static class MeilisearchQueryText
{
    public const int MaxCombinations = 32;

    /// <param name="fuzzyMinTermLength">The fuzzy pass: words at least this long may match close spellings.</param>
    public static IReadOnlyList<string> Build(ParsedSearchQuery query, int? fuzzyMinTermLength = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.IsEmpty)
            return [string.Empty];

        var exclusions = query.Clauses.Where(c => c.Negated).Select(c => "-" + Quoted(c.Alternatives[0])).ToList();
        var positive = query.Clauses.Where(c => !c.Negated).Select(c => c.Alternatives).ToList();

        return Combinations(positive)
            .Select(terms =>
            {
                var loose = terms.Where(t => IsLoose(t, fuzzyMinTermLength)).ToList();
                var exact = terms.Except(loose).Select(Quoted);
                return string.Join(' ', exact.Concat(exclusions).Concat(loose.Select(t => Clean(t.Words[0]))));
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Whether a fuzzy pass could match anything an exact pass did not: a single word long enough.</summary>
    public static bool AllowsFuzzy(ParsedSearchQuery query, int? fuzzyMinTermLength) =>
        fuzzyMinTermLength is { } min
        && query.Clauses.Where(c => !c.Negated)
            .SelectMany(c => c.Alternatives)
            .Any(t => !t.IsPhrase && !t.Prefix && t.Words[0].Length >= min);

    private static bool IsLoose(SearchTerm term, int? fuzzyMinTermLength) =>
        !term.IsPhrase && (term.Prefix || (fuzzyMinTermLength is { } min && term.Words[0].Length >= min));

    private static string Quoted(SearchTerm term) => "\"" + string.Join(' ', term.Words.Select(Clean)) + "\"";

    /// <summary>Normalized words never carry quotes; dropped anyway, so a word can never end a phrase early.</summary>
    private static string Clean(string word) => word.Replace("\"", string.Empty, StringComparison.Ordinal);

    private static IEnumerable<IReadOnlyList<SearchTerm>> Combinations(IReadOnlyList<IReadOnlyList<SearchTerm>> clauses)
    {
        IEnumerable<IReadOnlyList<SearchTerm>> combinations = [[]];
        foreach (var alternatives in clauses)
        {
            var current = combinations;
            combinations = current.SelectMany(prefix => alternatives.Select(term => (IReadOnlyList<SearchTerm>)[.. prefix, term]));
        }

        return combinations.Take(MaxCombinations);
    }
}
