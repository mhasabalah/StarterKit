namespace Catalog.Contracts.Categories;

// ── Commands (admin) ─────────────────────────────────────────────────────────

public record CreateCategoryCommand(
    LocalizedText Name,
    string Slug,
    Guid? ParentId = null,
    int DisplayOrder = 0)
    : ICommand<CreateCategoryResult>;

public record CreateCategoryResult(Guid Id);

/// <param name="IsActive">False deactivates the category (it disappears from the public tree).</param>
public record UpdateCategoryCommand(
    Guid Id,
    LocalizedText Name,
    string Slug,
    Guid? ParentId,
    int DisplayOrder,
    bool IsActive = true,
    long? Version = null)
    : ICommand<UpdateCategoryResult>;

public record UpdateCategoryResult(bool Success, long Version);

/// <summary>Lookups are never hard-deleted: "delete" deactivates, so existing products keep their category.</summary>
public record DeleteCategoryCommand(Guid Id) : ICommand<DeleteCategoryResult>;

public record DeleteCategoryResult(bool Success);

// ── Queries ──────────────────────────────────────────────────────────────────

/// <summary>Admin flat list (raw bilingual names). Inactive categories only when requested.</summary>
public record GetCategoriesQuery(bool IncludeInactive = false) : IQuery<List<CategoryDto>>;

/// <summary>Public, cached category tree (active categories only), names resolved for the request culture.</summary>
public record GetCategoryTreeQuery : IQuery<List<CategoryTreeNodeDto>>;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CategoryDto
{
    public Guid Id { get; init; }

    /// <summary>Raw bilingual name (admin editing).</summary>
    public LocalizedText Name { get; init; } = default!;

    /// <summary><see cref="Name"/> resolved for the request culture.</summary>
    public string DisplayName { get; init; } = string.Empty;

    public string Slug { get; init; } = default!;
    public Guid? ParentId { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; }
    public long Version { get; init; }
}

public record CategoryTreeNodeDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Slug { get; init; } = default!;
    public int DisplayOrder { get; init; }
    public List<CategoryTreeNodeDto> Children { get; init; } = [];
}
