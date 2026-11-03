namespace Catalog.Contracts.Products.Queries;

/// <summary>
/// Filters shared by the public and admin list queries. One <c>ProductQueryBuilder</c> applies them
/// so the two pipelines cannot drift apart.
/// </summary>
public interface IProductFilterQuery
{
    /// <summary>Case-insensitive match on name (en/ar), SKU or description. LIKE wildcards are escaped.</summary>
    string? Search { get; }

    ProductType? Type { get; }
    ProductStatus? Status { get; }
    Guid? CategoryId { get; }

    /// <summary>When true, <see cref="CategoryId"/> also matches every descendant category.</summary>
    bool? IncludeSubcategories { get; }

    decimal? MinPrice { get; }
    decimal? MaxPrice { get; }
    Currency? Currency { get; }

    /// <summary>The product must carry ALL of these tags.</summary>
    Guid[]? TagIds { get; }

    /// <summary>The product must have a value for ALL of these attribute types.</summary>
    Guid[]? AttributeTypeIds { get; }

    Guid? OwnerId { get; }
    bool? IsFeatured { get; }
}
