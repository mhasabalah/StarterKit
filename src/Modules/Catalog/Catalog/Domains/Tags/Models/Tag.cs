using Shared.Core.MultiTenancy;
namespace Catalog.Domains.Tags.Models;

/// <summary>
/// Simple bilingual lookup. Not an aggregate root with a repository: its admin CRUD handlers use
/// CatalogDbContext directly (the lightweight "lookup" pattern). Deactivated, never deleted.
/// </summary>
public class Tag : Entity<Guid>, IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    public const int NameMaxLength = 100;

    public LocalizedText Name { get; private set; } = default!;
    public bool IsActive { get; private set; }

    private Tag()
    {
    }

    public static Tag Create(LocalizedText name, Guid? id = null)
    {
        var tag = new Tag { Id = id ?? Guid.NewGuid(), IsActive = true };
        tag.Rename(name);
        return tag;
    }

    public void Rename(LocalizedText name)
    {
        if (name is null || string.IsNullOrWhiteSpace(name.En) || string.IsNullOrWhiteSpace(name.Ar))
            throw new BadRequestException("Tag name is required in English and Arabic.");
        if (name.En.Trim().Length > NameMaxLength || name.Ar.Trim().Length > NameMaxLength)
            throw new BadRequestException($"Tag name must not exceed {NameMaxLength} characters.");

        Name = LocalizedText.Create(name.En.Trim(), name.Ar.Trim());
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;
}
