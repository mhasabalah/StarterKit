using Catalog.Contracts.Products.Dtos;

namespace Catalog.Contracts.Products.Queries;

/// <summary>
/// Public detail. Visible when Published, or to its owner in any status. A read of a published product
/// by anyone other than its owner increments the view counter (atomic UPDATE, no version bump).
/// </summary>
public record GetProductByIdQuery(Guid Id, bool IncrementView = true) : IQuery<GetProductByIdResult>;

public record GetProductByIdResult(ProductDto Product);

/// <summary>Admin detail, including soft-deleted products.</summary>
public record GetAdminProductByIdQuery(Guid Id) : IQuery<GetAdminProductByIdResult>;

public record GetAdminProductByIdResult(ProductAdminDto Product);

/// <summary>Cross-module query with no endpoint of its own; the Api dashboard sends it.</summary>
public record GetProductStatsQuery : IQuery<ProductStatsDto>;
