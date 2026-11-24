using System.Text.RegularExpressions;

namespace Search.Engine;

/// <summary>
/// The indexes the modules registered (<c>AddSearchIndex</c>), validated once at startup: an invalid or duplicate
/// definition stops the host with every problem listed, so a broken index never reaches a deployment.
/// </summary>
public sealed partial class SearchIndexRegistry
{
    public const int MaxTextFields = 16;
    public const int MaxFilterFields = 32;
    public const int FieldNameMaxLength = 40;

    private readonly Dictionary<string, RegisteredIndex> _indexes = new(StringComparer.Ordinal);

    public SearchIndexRegistry(IEnumerable<SearchIndexDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var problems = new List<string>();

        foreach (var definition in definitions)
        {
            var errors = Validate(definition);
            if (errors.Count > 0)
            {
                problems.AddRange(errors.Select(e => $"Search index '{definition.Key}': {e}"));
                continue;
            }

            if (!_indexes.TryAdd(definition.Key, new RegisteredIndex(definition)))
                problems.Add($"Two search indexes use the key '{definition.Key}'.");
        }

        if (problems.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
    }

    public IReadOnlyCollection<RegisteredIndex> Indexes => _indexes.Values;

    public RegisteredIndex? Find(string key) => _indexes.GetValueOrDefault(key);

    private static List<string> Validate(SearchIndexDefinition definition)
    {
        var errors = new List<string>();

        if (definition.Key is null || definition.Key.Length > SearchIndexDefinition.KeyMaxLength || !KeyRegex().IsMatch(definition.Key))
        {
            errors.Add($"the key must look like 'module.entity' (lower case letters, digits, '_' and '-'; at most " +
                       $"{SearchIndexDefinition.KeyMaxLength} characters).");
        }

        if (definition.Name is null || string.IsNullOrWhiteSpace(definition.Name.En) || string.IsNullOrWhiteSpace(definition.Name.Ar))
            errors.Add("the name needs English and Arabic text.");

        var textFields = definition.TextFields ?? [];
        var filterFields = definition.FilterFields ?? [];

        if (textFields.Count == 0)
            errors.Add("at least one text field is required.");
        if (textFields.Count > MaxTextFields)
            errors.Add($"at most {MaxTextFields} text fields are allowed.");
        if (filterFields.Count > MaxFilterFields)
            errors.Add($"at most {MaxFilterFields} filter fields are allowed.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in textFields.Select(f => f.Name).Concat(filterFields.Select(f => f.Name)))
        {
            if (name is null || !FieldNameRegex().IsMatch(name))
                errors.Add($"field name '{name}' must be lower snake case (a letter, then letters, digits or '_'; at most {FieldNameMaxLength}).");
            else if (!names.Add(name))
                errors.Add($"field name '{name}' is used twice.");
        }

        foreach (var field in textFields)
        {
            if (!Enum.IsDefined(field.Weight))
                errors.Add($"text field '{field.Name}' has an unknown weight.");
            if (!Enum.IsDefined(field.Analyzer))
                errors.Add($"text field '{field.Name}' has an unknown analyzer.");
            if (field.MaxLength is < 1 or > 100_000)
                errors.Add($"text field '{field.Name}' needs a MaxLength between 1 and 100000.");
        }

        foreach (var field in filterFields)
        {
            if (!Enum.IsDefined(field.Type))
                errors.Add($"filter field '{field.Name}' has an unknown type.");
            if (field.Facetable && field.Type is not (SearchFieldType.Keyword or SearchFieldType.KeywordList or SearchFieldType.Boolean))
                errors.Add($"filter field '{field.Name}' cannot be facetable: only keyword, keyword list and boolean fields can.");
            if (field.Sortable && field.Type == SearchFieldType.KeywordList)
                errors.Add($"filter field '{field.Name}' cannot be sortable: a keyword list has no single value.");
        }

        if (definition.DefaultSort is { } sort && !filterFields.Any(f => f.Name == sort.Field && f.Sortable))
            errors.Add($"the default sort field '{sort.Field}' must be a sortable filter field.");

        return errors;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9_-]*)+$")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,39}$")]
    private static partial Regex FieldNameRegex();
}
