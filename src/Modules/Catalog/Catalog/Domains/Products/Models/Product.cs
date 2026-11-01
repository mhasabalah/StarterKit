using Catalog.Domains.Products.Events;

namespace Catalog.Domains.Products.Models;

/// <summary>
/// Product aggregate root. State changes only through the factory and behaviour methods,
/// which enforce invariants and raise domain events. Version (optimistic concurrency)
/// is inherited from <see cref="Entity{T}"/> and incremented centrally by the
/// AuditableEntityInterceptor — never touch it here.
/// </summary>
public class Product : Aggregate<Guid>
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int SkuMaxLength = 64;

    /// <summary>Largest value that fits the configured numeric(18,2) column.</summary>
    public const decimal MaxPrice = 9_999_999_999_999_999.99m;

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string Sku { get; private set; } = default!;
    public decimal Price { get; private set; }
    public bool IsActive { get; private set; }
    public Guid? ImageAttachmentId { get; private set; }

    // EF Core
    private Product()
    {
    }

    public static Product Create(
        string name,
        string? description,
        string sku,
        decimal price,
        bool isActive = true,
        Guid? imageAttachmentId = null)
    {
        var product = new Product { Id = Guid.NewGuid() };
        product.Apply(name, description, sku, price, imageAttachmentId);
        product.IsActive = isActive;

        product.AddDomainEvent(new ProductCreatedEvent(product.Id, product.Name, product.Sku));
        return product;
    }

    public void Update(
        string name,
        string? description,
        string sku,
        decimal price,
        bool isActive,
        Guid? imageAttachmentId)
    {
        Apply(name, description, sku, price, imageAttachmentId);
        IsActive = isActive;

        AddDomainEvent(new ProductUpdatedEvent(Id, Name, Sku, Price, IsActive));
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        AddDomainEvent(new ProductUpdatedEvent(Id, Name, Sku, Price, IsActive));
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        AddDomainEvent(new ProductUpdatedEvent(Id, Name, Sku, Price, IsActive));
    }

    /// <summary>Raises <see cref="ProductDeletedEvent"/>; the repository performs the removal.</summary>
    public void MarkDeleted()
    {
        AddDomainEvent(new ProductDeletedEvent(Id, ImageAttachmentId));
    }

    /// <summary>Canonical SKU form: trimmed, upper-case. Used for uniqueness checks.</summary>
    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    private void Apply(
        string name,
        string? description,
        string sku,
        decimal price,
        Guid? imageAttachmentId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidProductException("Product name is required.");
        if (name.Trim().Length > NameMaxLength)
            throw new InvalidProductException(
                $"Product name must not exceed {NameMaxLength} characters.");
        if (description is not null && description.Trim().Length > DescriptionMaxLength)
            throw new InvalidProductException(
                $"Product description must not exceed {DescriptionMaxLength} characters.");
        if (string.IsNullOrWhiteSpace(sku))
            throw new InvalidProductException("Product SKU is required.");
        if (sku.Trim().Length > SkuMaxLength)
            throw new InvalidProductException(
                $"Product SKU must not exceed {SkuMaxLength} characters.");
        if (price < 0)
            throw new InvalidProductException("Product price must not be negative.");
        if (price > MaxPrice)
            throw new InvalidProductException("Product price is too large.");
        if (imageAttachmentId == Guid.Empty)
            throw new InvalidProductException("Image attachment id must not be empty.");

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Sku = NormalizeSku(sku);
        Price = price;
        ImageAttachmentId = imageAttachmentId;
    }
}
