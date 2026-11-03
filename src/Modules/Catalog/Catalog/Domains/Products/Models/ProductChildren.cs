using Shared.Core.MultiTenancy;
namespace Catalog.Domains.Products.Models;

// Child entities of the Product aggregate. Only Product mutates them (internal members). Ids are
// assigned client-side and configured ValueGeneratedNever(), so EF tracks a child added to an
// already-loaded product as Added (not Modified) when DetectChanges discovers it.

public class ProductImage : Entity<Guid>, IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    public const int UrlMaxLength = 1000;
    public const int CaptionMaxLength = 300;

    public Guid ProductId { get; private set; }

    /// <summary>The Attachment-module file. Used for deletion — never match by URL.</summary>
    public Guid AttachmentId { get; private set; }

    public string Url { get; private set; } = default!;
    public string? Caption { get; private set; }
    public bool IsPrimary { get; private set; }
    public int DisplayOrder { get; private set; }

    private ProductImage()
    {
    }

    internal static ProductImage Create(
        Guid productId, Guid attachmentId, string url, string? caption, bool isPrimary, int displayOrder)
    {
        if (attachmentId == Guid.Empty)
            throw new InvalidProductException("Image attachment id must not be empty.");
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidProductException("Image URL is required.");
        if (url.Length > UrlMaxLength)
            throw new InvalidProductException($"Image URL must not exceed {UrlMaxLength} characters.");
        if (caption is not null && caption.Trim().Length > CaptionMaxLength)
            throw new InvalidProductException($"Image caption must not exceed {CaptionMaxLength} characters.");

        return new ProductImage
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AttachmentId = attachmentId,
            Url = url,
            Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim(),
            IsPrimary = isPrimary,
            DisplayOrder = displayOrder
        };
    }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;

    internal void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;
}

/// <summary>Join entity with payload: Product ⇄ AttributeType carrying a Value.</summary>
public class ProductAttribute : Entity<Guid>, IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    public const int ValueMaxLength = 500;

    public Guid ProductId { get; private set; }
    public Guid AttributeTypeId { get; private set; }
    public AttributeType? AttributeType { get; private set; }
    public string Value { get; private set; } = default!;

    private ProductAttribute()
    {
    }

    internal static ProductAttribute Create(Guid productId, Guid attributeTypeId, string value)
    {
        if (attributeTypeId == Guid.Empty)
            throw new InvalidProductException("Attribute type id must not be empty.");

        var attribute = new ProductAttribute
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AttributeTypeId = attributeTypeId
        };
        attribute.ChangeValue(value);
        return attribute;
    }

    internal void ChangeValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidProductException("Attribute value is required.");
        if (value.Trim().Length > ValueMaxLength)
            throw new InvalidProductException($"Attribute value must not exceed {ValueMaxLength} characters.");

        Value = value.Trim();
    }
}

/// <summary>Pure join entity: Product ⇄ Tag.</summary>
public class ProductTag : Entity<Guid>, IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    public Guid ProductId { get; private set; }
    public Guid TagId { get; private set; }
    public Tag? Tag { get; private set; }

    private ProductTag()
    {
    }

    internal static ProductTag Create(Guid productId, Guid tagId)
    {
        if (tagId == Guid.Empty)
            throw new InvalidProductException("Tag id must not be empty.");

        return new ProductTag { Id = Guid.NewGuid(), ProductId = productId, TagId = tagId };
    }
}
