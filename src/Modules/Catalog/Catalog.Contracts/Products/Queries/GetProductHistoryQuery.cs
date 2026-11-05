using Catalog.Contracts.Products.Dtos;

namespace Catalog.Contracts.Products.Queries;

/// <summary>
/// Audit trail of one product (who changed what, newest first). Includes soft-deleted products. Unknown id, or a
/// product of another tenant: not found.
/// </summary>
public record GetProductHistoryQuery(Guid ProductId) : BaseQuery, IQuery<PaginatedResult<ProductHistoryEntryDto>>;
