using Subscriptions.Contracts.Subscriptions.DTOs;
using Shared.Contracts.CQRS;
using Shared.Contracts.Pagination;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetSubscriptionInvoicesQuery : BaseQuery, IQuery<PaginatedResult<InvoiceDto>>
{
    public Guid SubscriptionId { get; init; }
    public Guid SubscriberId { get; init; }
}
