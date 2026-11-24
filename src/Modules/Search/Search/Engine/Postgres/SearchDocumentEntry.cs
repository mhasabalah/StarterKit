using NpgsqlTypes;

namespace Search.Engine.Postgres;

/// <summary>
/// A document of the PostgreSQL engine (<c>search.search_documents</c>). Mapped so the schema, its GIN indexes and its
/// row-level security live in migrations like every other table, but written only by
/// <see cref="PostgresSearchEngine"/> in raw SQL (the vector is built by PostgreSQL from the normalized text).
/// </summary>
public sealed class SearchDocumentEntry : IMustHaveTenant
{
    public const int SuggestTextMaxLength = 500;

    public Guid TenantId { get; set; }

    public string IndexKey { get; set; } = default!;

    public string DocumentId { get; set; } = default!;

    public Guid? BranchId { get; set; }

    /// <summary>The source's version: an older write never replaces a newer one.</summary>
    public long SourceVersion { get; set; }

    /// <summary>The rebuild that wrote the row (the sweep removes rows a rebuild did not reach).</summary>
    public long RebuildStamp { get; set; }

    public NpgsqlTsVector SearchVector { get; set; } = default!;

    /// <summary>The normalized suggest fields, for the trigram (typo-tolerant) fallback.</summary>
    public string SuggestText { get; set; } = string.Empty;

    /// <summary>Filter values: <c>{"status":"Published","tag_ids":["a"],"price":10,"published_at":1730000000000}</c>.</summary>
    public string Filters { get; set; } = "{}";

    /// <summary>The original text of the highlight fields, by field name.</summary>
    public string Content { get; set; } = "{}";

    public DateTime IndexedAt { get; set; }
}
