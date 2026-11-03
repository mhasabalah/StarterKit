using Shared.Core.MultiTenancy;
using System.Text.RegularExpressions;
using Catalog.Domains.Categories.Events;
using Catalog.Domains.Categories.Exceptions;

namespace Catalog.Domains.Categories.Models;

/// <summary>
/// Hierarchical lookup (optional self-referencing <see cref="ParentId"/>). An aggregate because changes
/// raise <see cref="CategoryChangedEvent"/>, which invalidates the cached category tree. Never hard-deleted:
/// "delete" deactivates, so products keep their category.
/// </summary>
public partial class Category : Aggregate<Guid>, IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    public const int NameMaxLength = 150;
    public const int SlugMaxLength = 100;
    public const string SlugPattern = "^[a-z0-9]+(?:-[a-z0-9]+)*$";

    public LocalizedText Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public Guid? ParentId { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    private Category()
    {
    }

    public static Category Create(LocalizedText name, string slug, Guid? parentId = null, int displayOrder = 0,
        Guid? id = null)
    {
        var category = new Category { Id = id ?? Guid.NewGuid(), IsActive = true };
        category.Apply(name, slug, parentId, displayOrder);
        category.AddDomainEvent(new CategoryChangedEvent(category.Id));
        return category;
    }

    /// <summary>Cycle detection across the whole tree is the handler's job (needs all categories).</summary>
    public void Update(LocalizedText name, string slug, Guid? parentId, int displayOrder)
    {
        Apply(name, slug, parentId, displayOrder);
        AddDomainEvent(new CategoryChangedEvent(Id));
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        AddDomainEvent(new CategoryChangedEvent(Id));
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        AddDomainEvent(new CategoryChangedEvent(Id));
    }

    public static string NormalizeSlug(string slug) => slug.Trim().ToLowerInvariant();

    /// <summary>Validates the normalized form (input is lower-cased and trimmed before storage).</summary>
    public static bool IsValidSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return false;
        var normalized = NormalizeSlug(slug);
        return normalized.Length <= SlugMaxLength && SlugRegex().IsMatch(normalized);
    }

    private void Apply(LocalizedText name, string slug, Guid? parentId, int displayOrder)
    {
        if (name is null || string.IsNullOrWhiteSpace(name.En) || string.IsNullOrWhiteSpace(name.Ar))
            throw new InvalidCategoryException("Category name is required in English and Arabic.");
        if (name.En.Trim().Length > NameMaxLength || name.Ar.Trim().Length > NameMaxLength)
            throw new InvalidCategoryException($"Category name must not exceed {NameMaxLength} characters.");

        if (!IsValidSlug(slug))
            throw new InvalidCategoryException("Category slug must be lowercase letters, digits and single hyphens.");
        if (parentId == Guid.Empty)
            throw new InvalidCategoryException("Parent id must not be empty.");
        if (parentId == Id)
            throw new InvalidCategoryException("A category cannot be its own parent.");
        if (displayOrder < 0)
            throw new InvalidCategoryException("Display order must not be negative.");

        Name = LocalizedText.Create(name.En.Trim(), name.Ar.Trim());
        Slug = NormalizeSlug(slug);
        ParentId = parentId;
        DisplayOrder = displayOrder;
    }

    [GeneratedRegex(SlugPattern)]
    private static partial Regex SlugRegex();
}
