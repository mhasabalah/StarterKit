using Shared.Contracts.CQRS;
using Shared.Contracts.Pagination;
using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetMySubscriptionInvoicesQuery : BaseQuery, IQuery<PaginatedResult<InvoiceDto>>
{
    public Guid? SubscriberId { get; init; }
}
