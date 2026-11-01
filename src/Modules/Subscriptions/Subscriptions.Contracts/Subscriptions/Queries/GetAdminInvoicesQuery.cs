using Subscriptions.Contracts.Subscriptions.DTOs;
using Shared.Contracts.CQRS;
using Shared.Contracts.Pagination;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetAdminInvoicesQuery : BaseQuery, IQuery<PaginatedResult<AdminInvoiceDto>>
{
    public Guid? SubscriberId { get; init; }
    public int? Status { get; init; }
    public int? Type { get; init; }
}
