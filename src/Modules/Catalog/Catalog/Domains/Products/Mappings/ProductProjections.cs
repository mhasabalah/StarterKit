namespace Catalog.Domains.Products.Mappings;

/// <summary>
/// One flat SQL projection of a product used by every product query (public, owner, admin). Sorting
/// (OrderByDynamic) and paging run on this row in SQL; the row is then mapped in memory to the
/// audience-specific DTO by <see cref="ProductDtoMapper"/>. One projection instead of one per DTO keeps
/// the queries DRY. Names are chosen per culture in SQL (<c>arabic ? Ar : En</c>, a query parameter).
/// </summary>
public sealed record ProductRow
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string NameEn { get; init; } = default!;
    public string NameAr { get; init; } = default!;
    public string? Description { get; init; }
    public string Sku { get; init; } = default!;
    public ProductType Type { get; init; }
    public ProductStatus Status { get; init; }
    public decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public Currency Currency { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public List<ProductImageDto> Images { get; init; } = [];
    public ProductImageDto? PrimaryImage { get; init; }
    public List<ProductAttributeDto> Attributes { get; init; } = [];
    public List<ProductTagDto> Tags { get; init; } = [];
    public Guid OwnerId { get; init; }
    public int ViewCount { get; init; }
    public bool IsFeatured { get; init; }
    public bool IsWishlisted { get; init; }
    public DateTime? PublishedAt { get; init; }
    public DateTime? CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? LastModified { get; init; }
    public long Version { get; init; }
    public string? ReviewNotes { get; init; }
    public DateTime? SubmittedForReviewAt { get; init; }
    public Guid? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
}

public static class ProductProjections
{
    /// <param name="currentUserId">Guid.Empty for anonymous callers (never matches a wishlist row).</param>
    /// <param name="includeGallery">Detail views: all images. List views: none (the primary image only).</param>
    public static IQueryable<ProductRow> ToProductRows(
        this IQueryable<Product> products,
        IQueryable<WishlistItem> wishlistItems,
        Guid currentUserId,
        bool includeGallery)
    {
        var arabic = CatalogCulture.IsArabic();
        var imageLimit = includeGallery ? Product.MaxImages : 0;

        return products.Select(p => new ProductRow
        {
            Id = p.Id,
            Name = arabic ? p.Name.Ar : p.Name.En,
            NameEn = p.Name.En,
            NameAr = p.Name.Ar,
            Description = p.Description,
            Sku = p.Sku,
            Type = p.Type,
            Status = p.Status,
            Price = p.Pricing.Price,
            CompareAtPrice = p.Pricing.CompareAtPrice,
            Currency = p.Pricing.Currency,
            CategoryId = p.CategoryId,
            CategoryName = p.Category == null ? null : arabic ? p.Category.Name.Ar : p.Category.Name.En,
            Images = p.Images
                .OrderBy(i => i.DisplayOrder)
                .Take(imageLimit)
                .Select(i => new ProductImageDto
                {
                    Id = i.Id,
                    AttachmentId = i.AttachmentId,
                    Url = i.Url,
                    Caption = i.Caption,
                    IsPrimary = i.IsPrimary,
                    DisplayOrder = i.DisplayOrder
                })
                .ToList(),
            PrimaryImage = p.Images
                .Where(i => i.IsPrimary)
                .Select(i => new ProductImageDto
                {
                    Id = i.Id,
                    AttachmentId = i.AttachmentId,
                    Url = i.Url,
                    Caption = i.Caption,
                    IsPrimary = i.IsPrimary,
                    DisplayOrder = i.DisplayOrder
                })
                .FirstOrDefault(),
            Attributes = p.Attributes
                .Select(a => new ProductAttributeDto
                {
                    AttributeTypeId = a.AttributeTypeId,
                    Name = arabic ? a.AttributeType!.Name.Ar : a.AttributeType!.Name.En,
                    DataType = a.AttributeType!.DataType,
                    Unit = a.AttributeType!.Unit,
                    Value = a.Value
                })
                .ToList(),
            Tags = p.Tags
                .Select(t => new ProductTagDto
                {
                    TagId = t.TagId,
                    Name = arabic ? t.Tag!.Name.Ar : t.Tag!.Name.En
                })
                .ToList(),
            OwnerId = p.OwnerId,
            ViewCount = p.ViewCount,
            IsFeatured = p.IsFeatured,
            IsWishlisted = wishlistItems.Any(w => w.UserId == currentUserId && w.ProductId == p.Id),
            PublishedAt = p.PublishedAt,
            CreatedAt = p.CreatedAt,
            CreatedBy = p.CreatedBy,
            LastModified = p.LastModified,
            Version = p.Version,
            ReviewNotes = p.ReviewNotes,
            SubmittedForReviewAt = p.SubmittedForReviewAt,
            ReviewedBy = p.ReviewedBy,
            ReviewedAt = p.ReviewedAt,
            IsDeleted = p.IsDeleted,
            DeletedAt = p.DeletedAt
        });
    }

    /// <summary>Default list order (newest first) + sortable columns of <see cref="ProductRow"/> + Id tie-breaker.</summary>
    public static IQueryable<ProductRow> Sorted(this IQueryable<ProductRow> rows, BaseSortQuery sort) =>
        rows.ApplySort(sort, q => q.OrderByDescending(r => r.CreatedAt), r => r.Id);
}
