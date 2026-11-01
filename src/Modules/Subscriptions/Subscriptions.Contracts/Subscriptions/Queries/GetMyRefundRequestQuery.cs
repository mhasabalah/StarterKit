using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetMyRefundRequestQuery(Guid SubscriberId, Guid SubscriptionId) : IQuery<RefundRequestDto?>;
