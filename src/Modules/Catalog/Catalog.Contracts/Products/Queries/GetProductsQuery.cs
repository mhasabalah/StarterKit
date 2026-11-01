using Catalog.Contracts.Products.Dtos;
using Shared.Contracts.Pagination;

namespace Catalog.Contracts.Products.Queries;

/// <summary>
/// Paginated product listing. Inherits PageNumber/PageSize/SortBy/SortColumn from
/// <see cref="BaseQuery"/>. <see cref="Search"/> matches Name or Sku (case-insensitive).
/// </summary>
public record GetProductsQuery : BaseQuery, IQuery<PaginatedResult<ProductDto>>
{
    public string? Search { get; init; }
    public bool? IsActive { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
}
