namespace Search.Contracts.Indexing;

/// <summary>
/// The searchable projection of one entity, as its owning module's <see cref="ISearchDocumentSource"/> builds it.
/// Built only through <see cref="For(Guid, long)"/>; Search checks it against the index definition before indexing.
/// </summary>
/// <example>
/// <code>
/// SearchDocument.For(product.Id, product.Version)
///     .InBranch(product.BranchId)
///     .Text("name_en", product.Name.En)
///     .Keyword("status", product.Status.ToString())
///     .Number("price", product.Price)
///     .Build();
/// </code>
/// </example>
public sealed class SearchDocument
{
    internal SearchDocument(
        string id, long version, Guid? branchId,
        IReadOnlyDictionary<string, string> text, IReadOnlyDictionary<string, SearchValue> filters)
    {
        Id = id;
        Version = version;
        BranchId = branchId;
        Text = text;
        Filters = filters;
    }

    /// <summary>The entity id (letters, digits, <c>-</c>, <c>_</c>; at most <see cref="IdMaxLength"/>).</summary>
    public string Id { get; }

    /// <summary>
    /// A number that grows with every change of the entity (its aggregate <c>Version</c>): an older document never
    /// overwrites a newer one, and drift detection compares it with the index.
    /// </summary>
    public long Version { get; }

    /// <summary>The branch that owns the entity; <c>null</c> = visible to the whole tenant.</summary>
    public Guid? BranchId { get; }

    /// <summary>Text field values by field name (empty values are left out).</summary>
    public IReadOnlyDictionary<string, string> Text { get; }

    /// <summary>Filter field values by field name (missing values are left out).</summary>
    public IReadOnlyDictionary<string, SearchValue> Filters { get; }

    public const int IdMaxLength = 100;

    /// <summary>Starts a document for an entity with a Guid id.</summary>
    public static SearchDocumentBuilder For(Guid id, long version) => new(id.ToString(), version);

    /// <summary>Starts a document for an entity with a string id.</summary>
    public static SearchDocumentBuilder For(string id, long version) => new(id, version);

    internal static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= IdMaxLength && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>Fluent builder of a <see cref="SearchDocument"/>. A <c>null</c> value leaves the field out.</summary>
public sealed class SearchDocumentBuilder
{
    private readonly string _id;
    private readonly long _version;
    private Guid? _branchId;
    private readonly Dictionary<string, string> _text = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SearchValue> _filters = new(StringComparer.Ordinal);

    internal SearchDocumentBuilder(string id, long version)
    {
        if (!SearchDocument.IsValidId(id))
        {
            throw new ArgumentException(
                $"A search document id must be 1-{SearchDocument.IdMaxLength} letters, digits, '-' or '_'; got '{id}'.", nameof(id));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(version);
        _id = id;
        _version = version;
    }

    /// <summary>The owning branch; <c>null</c> (the default) = tenant-wide.</summary>
    public SearchDocumentBuilder InBranch(Guid? branchId)
    {
        _branchId = branchId;
        return this;
    }

    public SearchDocumentBuilder Text(string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _text[CheckNew(field)] = value;
        return this;
    }

    public SearchDocumentBuilder Keyword(string field, string? value) =>
        value is null ? this : Set(field, new KeywordValue(value));

    public SearchDocumentBuilder Keyword(string field, Guid? value) =>
        value is null ? this : Set(field, new KeywordValue(value.Value.ToString()));

    public SearchDocumentBuilder Keywords(string field, IEnumerable<string>? values) =>
        values is null ? this : Set(field, new KeywordListValue([.. values.Distinct(StringComparer.Ordinal)]));

    public SearchDocumentBuilder Keywords(string field, IEnumerable<Guid>? values) =>
        values is null ? this : Keywords(field, values.Select(v => v.ToString()));

    public SearchDocumentBuilder Number(string field, decimal? value) =>
        value is null ? this : Set(field, new NumberValue(value.Value));

    /// <summary>A UTC date and time (<see cref="DateTimeKind.Utc"/>).</summary>
    public SearchDocumentBuilder Date(string field, DateTime? value)
    {
        if (value is null)
            return this;
        if (value.Value.Kind != DateTimeKind.Utc)
            throw new ArgumentException($"Search date field '{field}' must be UTC.", nameof(value));
        return Set(field, new DateValue(value.Value));
    }

    public SearchDocumentBuilder Bool(string field, bool? value) =>
        value is null ? this : Set(field, new BooleanValue(value.Value));

    public SearchDocument Build() => new(_id, _version, _branchId, _text.AsReadOnly(), _filters.AsReadOnly());

    private SearchDocumentBuilder Set(string field, SearchValue value)
    {
        _filters[CheckNew(field)] = value;
        return this;
    }

    private string CheckNew(string field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        if (_text.ContainsKey(field) || _filters.ContainsKey(field))
            throw new ArgumentException($"Search document field '{field}' is set twice.", nameof(field));
        return field;
    }
}

/// <summary>A typed filter value of a <see cref="SearchDocument"/>.</summary>
public abstract record SearchValue
{
    private protected SearchValue()
    {
    }

    /// <summary>The field type this value is valid for.</summary>
    public abstract SearchFieldType Type { get; }

    /// <summary>Whether <paramref name="type"/> accepts this value.</summary>
    public bool Fits(SearchFieldType type) => Type == type;
}

public sealed record KeywordValue(string Value) : SearchValue
{
    public override SearchFieldType Type => SearchFieldType.Keyword;
}

public sealed record KeywordListValue(IReadOnlyList<string> Values) : SearchValue
{
    public override SearchFieldType Type => SearchFieldType.KeywordList;

    public bool Equals(KeywordListValue? other) =>
        other is not null && Values.SequenceEqual(other.Values, StringComparer.Ordinal);

    public override int GetHashCode() => Values.Aggregate(0, (hash, v) => HashCode.Combine(hash, v));
}

public sealed record NumberValue(decimal Value) : SearchValue
{
    public override SearchFieldType Type => SearchFieldType.Number;
}

public sealed record DateValue(DateTime Value) : SearchValue
{
    public override SearchFieldType Type => SearchFieldType.Date;
}

public sealed record BooleanValue(bool Value) : SearchValue
{
    public override SearchFieldType Type => SearchFieldType.Boolean;
}
