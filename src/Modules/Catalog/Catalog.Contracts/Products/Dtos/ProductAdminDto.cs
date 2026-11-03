namespace Catalog.Contracts.Products.Dtos;

/// <summary>
/// Admin view: everything, including the raw bilingual name (<see cref="LocalizedName"/>), reviewer
/// history, audit fields and soft-delete state.
/// </summary>
public record ProductAdminDto : MyProductDto
{
    public LocalizedText LocalizedName { get; init; } = default!;
    public Guid? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? LastModified { get; init; }
}

/// <summary>Moderation-queue row.</summary>
public record PendingProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Sku { get; init; } = default!;
    public ProductType Type { get; init; }
    public string TypeLabel { get; init; } = string.Empty;
    public ProductStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public Currency Currency { get; init; }
    public Guid OwnerId { get; init; }
    public DateTime? SubmittedForReviewAt { get; init; }
    public int DaysPending { get; init; }
    public long Version { get; init; }
}

/// <summary>Cross-module stats, consumed by the admin dashboard via <c>GetProductStatsQuery</c>.</summary>
public record ProductStatsDto(
    int TotalProducts,
    int PendingReviewProducts,
    int PublishedProducts,
    int FeaturedProducts);
