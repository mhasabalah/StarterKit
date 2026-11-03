namespace Catalog.Contracts.Products.Dtos;

/// <summary>
/// Public read model. Names are resolved for the request culture (en/ar); enum values come with a
/// localized <c>*Label</c>. <see cref="Version"/> must be round-tripped on update for optimistic
/// concurrency (stale version → HTTP 409).
/// </summary>
public record ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
    public string Sku { get; init; } = default!;
    public ProductType Type { get; init; }
    public string TypeLabel { get; init; } = string.Empty;

    // Pricing (owned value object, flattened so clients can sort on it)
    public decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public decimal DiscountPercentage { get; init; }
    public Currency Currency { get; init; }
    public string CurrencyLabel { get; init; } = string.Empty;

    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }

    /// <summary>List views: empty (only <see cref="PrimaryImage"/>). Detail views: the full gallery.</summary>
    public List<ProductImageDto> Images { get; init; } = [];
    public ProductImageDto? PrimaryImage { get; init; }
    public List<ProductAttributeDto> Attributes { get; init; } = [];
    public List<ProductTagDto> Tags { get; init; } = [];

    public Guid OwnerId { get; init; }
    public int ViewCount { get; init; }
    public bool IsFeatured { get; init; }

    /// <summary>True when the current (authenticated) user has the product in their wishlist.</summary>
    public bool IsWishlisted { get; init; }

    public DateTime? PublishedAt { get; init; }
    public DateTime? CreatedAt { get; init; }
    public long Version { get; init; }
}

public record ProductImageDto
{
    public Guid Id { get; init; }
    public Guid AttachmentId { get; init; }
    public string Url { get; init; } = default!;
    public string? Caption { get; init; }
    public bool IsPrimary { get; init; }
    public int DisplayOrder { get; init; }
}

public record ProductAttributeDto
{
    public Guid AttributeTypeId { get; init; }
    public string Name { get; init; } = default!;
    public AttributeDataType DataType { get; init; }
    public string? Unit { get; init; }
    public string Value { get; init; } = default!;
}

public record ProductTagDto
{
    public Guid TagId { get; init; }
    public string Name { get; init; } = default!;
}
