using Catalog.Contracts.Products.Dtos;

namespace Catalog.Contracts.Products.Queries;

/// <summary>Public catalogue. The handler always restricts to Published products, whatever Status says.</summary>
public record GetProductsQuery : BaseQuery, IQuery<PaginatedResult<ProductDto>>, IProductFilterQuery
{
    public string? Search { get; init; }
    public ProductType? Type { get; init; }
    public ProductStatus? Status { get; init; }
    public Guid? CategoryId { get; init; }
    public bool? IncludeSubcategories { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public Currency? Currency { get; init; }
    public Guid[]? TagIds { get; init; }
    public Guid[]? AttributeTypeIds { get; init; }
    public Guid? OwnerId { get; init; }
    public bool? IsFeatured { get; init; }
}

/// <summary>Admin listing: any status, optionally including soft-deleted products.</summary>
public record GetAdminProductsQuery : BaseQuery, IQuery<PaginatedResult<ProductAdminDto>>, IProductFilterQuery
{
    public string? Search { get; init; }
    public ProductType? Type { get; init; }
    public ProductStatus? Status { get; init; }
    public Guid? CategoryId { get; init; }
    public bool? IncludeSubcategories { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public Currency? Currency { get; init; }
    public Guid[]? TagIds { get; init; }
    public Guid[]? AttributeTypeIds { get; init; }
    public Guid? OwnerId { get; init; }
    public bool? IsFeatured { get; init; }
    public bool IncludeDeleted { get; init; }
}

/// <summary>Dedicated search endpoint (name en/ar, SKU, description). Published products only.</summary>
public record SearchProductsQuery : BaseQuery, IQuery<PaginatedResult<ProductDto>>
{
    public string SearchTerm { get; init; } = string.Empty;
}

/// <summary>The current user's own products (any status) plus aggregate stats.</summary>
public record GetMyProductsQuery : BaseQuery, IQuery<GetMyProductsResult>
{
    public ProductStatus? Status { get; init; }
    public ProductType? Type { get; init; }
    public string? Search { get; init; }
}

public record GetMyProductsResult(PaginatedResult<MyProductDto> Products, MyProductStatsDto Stats);

/// <summary>Admin moderation queue. Defaults to PendingReview, oldest submission first.</summary>
public record GetPendingProductsQuery : BaseQuery, IQuery<PaginatedResult<PendingProductDto>>
{
    public string? Search { get; init; }
    public ProductStatus? Status { get; init; }
}
