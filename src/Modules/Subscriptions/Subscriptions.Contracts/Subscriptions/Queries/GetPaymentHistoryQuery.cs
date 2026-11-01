using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPaymentHistoryQuery : BaseQuery, IQuery<PaginatedResult<PaymentHistoryDto>>
{
    public Guid SubscriberId { get; init; }
}
