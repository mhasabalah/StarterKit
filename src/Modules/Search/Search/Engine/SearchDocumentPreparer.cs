using Search.Engine.Text;

namespace Search.Engine;

/// <summary>
/// Turns an owner's <see cref="SearchDocument"/> into an <see cref="EngineDocument"/>: checks it against the index,
/// cuts text to each field's <c>MaxLength</c>, normalizes it, and collects the suggest text and the highlight originals.
/// The same preparation feeds every engine.
/// </summary>
public static class SearchDocumentPreparer
{
    /// <exception cref="InvalidSearchDocumentException">The document does not fit the index (a bug in the source).</exception>
    public static EngineDocument Prepare(RegisteredIndex index, SearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(document);

        var problems = index.Validate(document);
        if (problems.Count > 0)
            throw new InvalidSearchDocumentException(index.Key, document.Id, problems);

        var text = new Dictionary<string, string>(StringComparer.Ordinal);
        var originals = new Dictionary<string, string>(StringComparer.Ordinal);
        var suggest = new List<string>();

        foreach (var field in index.Definition.TextFields)
        {
            if (!document.Text.TryGetValue(field.Name, out var value))
                continue;

            var original = Cut(value, field.MaxLength);
            var normalized = SearchTextNormalizer.Normalize(original).Value;
            if (normalized.Length == 0)
                continue;

            text[field.Name] = normalized;
            if (field.Suggest)
                suggest.Add(normalized);
            if (field.Highlight)
                originals[field.Name] = original;
        }

        var suggestText = Cut(string.Join(' ', suggest), Postgres.SearchDocumentEntry.SuggestTextMaxLength);
        return new EngineDocument(document.Id, document.BranchId, document.Version, text, suggestText, document.Filters, originals);
    }

    /// <summary>Cuts to at most <paramref name="max"/> characters without splitting a surrogate pair.</summary>
    internal static string Cut(string value, int max)
    {
        if (value.Length <= max)
            return value;
        var end = char.IsLowSurrogate(value[max]) ? max - 1 : max;
        return value[..end];
    }
}

/// <summary>A source returned a document that does not fit its index definition.</summary>
public sealed class InvalidSearchDocumentException(string indexKey, string documentId, IReadOnlyList<string> problems)
    : InvalidOperationException($"Search document '{documentId}' of '{indexKey}' does not fit the index: {string.Join(" ", problems)}")
{
    public string IndexKey { get; } = indexKey;

    public string DocumentId { get; } = documentId;

    public IReadOnlyList<string> Problems { get; } = problems;
}
