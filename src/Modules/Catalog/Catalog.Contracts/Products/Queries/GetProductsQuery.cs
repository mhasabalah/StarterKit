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

/// <summary>
/// Dedicated search endpoint over published products (name en/ar, SKU, description), with the catalogue filters.
/// With the search index: relevance order, stemming, Arabic normalization, close spellings, <see cref="Facets"/> and
/// <see cref="Highlight"/>; otherwise a case-insensitive "contains" match (see <see cref="SearchProductsResult.Match"/>).
/// </summary>
public record SearchProductsQuery : BaseQuery, IQuery<SearchProductsResult>, IProductFilterQuery
{
    public string SearchTerm { get; init; } = string.Empty;

    public ProductType? Type { get; init; }
    public Guid? CategoryId { get; init; }
    public bool? IncludeSubcategories { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public Currency? Currency { get; init; }
    public Guid[]? TagIds { get; init; }
    public Guid[]? AttributeTypeIds { get; init; }
    public Guid? OwnerId { get; init; }
    public bool? IsFeatured { get; init; }

    /// <summary>Value counts to return over all matches (needs the search index; empty otherwise).</summary>
    public ProductFacet[]? Facets { get; init; }

    /// <summary>Return snippets of the name and description with the matches in <c>&lt;mark&gt;</c> (search index only).</summary>
    public bool? Highlight { get; init; }

    string? IProductFilterQuery.Search => SearchTerm;

    /// <summary>Always published: the public search never sees other statuses.</summary>
    ProductStatus? IProductFilterQuery.Status => ProductStatus.Published;
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
