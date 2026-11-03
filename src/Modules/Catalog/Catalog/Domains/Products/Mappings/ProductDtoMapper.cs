namespace Catalog.Domains.Products.Mappings;

/// <summary>
/// In-memory step after the SQL projection (the "PropertyDtoLocalizer" idea): adds the culture-specific
/// enum labels and the computed discount, and builds the audience-specific DTO from one row type.
/// </summary>
public sealed class ProductDtoMapper(IEnumLocalizationService enumLocalization)
{
    public ProductDto ToPublic(ProductRow row) => Fill(new ProductDto(), row);

    public MyProductDto ToMine(ProductRow row) => FillOwner(new MyProductDto(), row);

    public ProductAdminDto ToAdmin(ProductRow row) => FillOwner(new ProductAdminDto(), row) with
    {
        LocalizedName = LocalizedText.Create(row.NameEn, row.NameAr),
        ReviewedBy = row.ReviewedBy,
        ReviewedAt = row.ReviewedAt,
        IsDeleted = row.IsDeleted,
        DeletedAt = row.DeletedAt,
        CreatedBy = row.CreatedBy,
        LastModified = row.LastModified
    };

    public PendingProductDto ToPending(ProductRow row, DateTime utcNow) => new()
    {
        Id = row.Id,
        Name = row.Name,
        Sku = row.Sku,
        Type = row.Type,
        TypeLabel = enumLocalization.Localize(row.Type),
        Status = row.Status,
        StatusLabel = enumLocalization.Localize(row.Status),
        Price = row.Price,
        Currency = row.Currency,
        OwnerId = row.OwnerId,
        SubmittedForReviewAt = row.SubmittedForReviewAt,
        DaysPending = row.SubmittedForReviewAt is { } submitted ? Math.Max(0, (utcNow - submitted).Days) : 0,
        Version = row.Version
    };

    private TDto FillOwner<TDto>(TDto dto, ProductRow row) where TDto : MyProductDto =>
        Fill(dto, row) with
        {
            Status = row.Status,
            StatusLabel = enumLocalization.Localize(row.Status),
            ReviewNotes = row.ReviewNotes,
            SubmittedForReviewAt = row.SubmittedForReviewAt
        };

    private TDto Fill<TDto>(TDto dto, ProductRow row) where TDto : ProductDto => dto with
    {
        Id = row.Id,
        Name = row.Name,
        Description = row.Description,
        Sku = row.Sku,
        Type = row.Type,
        TypeLabel = enumLocalization.Localize(row.Type),
        Price = row.Price,
        CompareAtPrice = row.CompareAtPrice,
        DiscountPercentage = Pricing.CalculateDiscountPercentage(row.Price, row.CompareAtPrice),
        Currency = row.Currency,
        CurrencyLabel = enumLocalization.Localize(row.Currency),
        CategoryId = row.CategoryId,
        CategoryName = row.CategoryName,
        Images = row.Images,
        PrimaryImage = row.PrimaryImage,
        Attributes = row.Attributes,
        Tags = row.Tags,
        OwnerId = row.OwnerId,
        ViewCount = row.ViewCount,
        IsFeatured = row.IsFeatured,
        IsWishlisted = row.IsWishlisted,
        PublishedAt = row.PublishedAt,
        CreatedAt = row.CreatedAt,
        Version = row.Version
    };
}
