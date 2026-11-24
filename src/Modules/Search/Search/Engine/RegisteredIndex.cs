using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Search.Engine;

/// <summary>
/// A validated <see cref="SearchIndexDefinition"/> with its field lookups and fingerprint. Engines and the indexing
/// pipeline work with this, never with the raw definition.
/// </summary>
public sealed class RegisteredIndex
{
    /// <summary>
    /// The version of the stored format: the text normalization rules and how fields map to the engines. Part of
    /// <see cref="Fingerprint"/>, so changing it rebuilds every index.
    /// </summary>
    public const int FormatVersion = 1;

    internal RegisteredIndex(SearchIndexDefinition definition)
    {
        Definition = definition;
        TextFields = definition.TextFields.ToDictionary(f => f.Name, StringComparer.Ordinal).AsReadOnly();
        FilterFields = definition.FilterFields.ToDictionary(f => f.Name, StringComparer.Ordinal).AsReadOnly();
        Fingerprint = ComputeFingerprint(definition);
    }

    public SearchIndexDefinition Definition { get; }

    public string Key => Definition.Key;

    public IReadOnlyDictionary<string, SearchTextField> TextFields { get; }

    public IReadOnlyDictionary<string, SearchFilterField> FilterFields { get; }

    /// <summary>
    /// SHA-256 (hex) of everything that shapes the stored index: fields, weights, analyzers, flags, the default sort and
    /// <see cref="FormatVersion"/>. Not the display name. A changed fingerprint means the index must be rebuilt.
    /// </summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Problems of <paramref name="document"/> against this index: unknown fields and values of the wrong type. An
    /// empty list means it can be indexed (text longer than a field's <c>MaxLength</c> is cut, not rejected).
    /// </summary>
    public IReadOnlyList<string> Validate(SearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var problems = new List<string>();

        foreach (var name in document.Text.Keys.Where(name => !TextFields.ContainsKey(name)))
            problems.Add($"'{name}' is not a text field of '{Key}'.");

        foreach (var (name, value) in document.Filters)
        {
            if (!FilterFields.TryGetValue(name, out var field))
                problems.Add($"'{name}' is not a filter field of '{Key}'.");
            else if (!value.Fits(field.Type))
                problems.Add($"'{name}' of '{Key}' takes a {field.Type} value, not a {value.Type}.");
        }

        return problems;
    }

    private static string ComputeFingerprint(SearchIndexDefinition definition)
    {
        // A canonical, culture-invariant description; field order matters (it is the declaration order).
        var text = new StringBuilder()
            .Append("v=").Append(FormatVersion.ToString(CultureInfo.InvariantCulture))
            .Append("|key=").Append(definition.Key);

        foreach (var f in definition.TextFields)
        {
            text.Append("|t:").Append(f.Name).Append(':').Append(f.Weight).Append(':').Append(f.Analyzer)
                .Append(':').Append(f.Suggest ? 's' : '-').Append(f.Highlight ? 'h' : '-')
                .Append(':').Append(f.MaxLength.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var f in definition.FilterFields)
        {
            text.Append("|f:").Append(f.Name).Append(':').Append(f.Type)
                .Append(':').Append(f.Facetable ? 'f' : '-').Append(f.Sortable ? 's' : '-');
        }

        if (definition.DefaultSort is { } sort)
            text.Append("|sort:").Append(sort.Field).Append(':').Append(sort.Descending ? "desc" : "asc");

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
