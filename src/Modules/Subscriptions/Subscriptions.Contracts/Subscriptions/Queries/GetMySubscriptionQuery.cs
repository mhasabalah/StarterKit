using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetMySubscriptionQuery(Guid SubscriberId) : IQuery<SubscriptionDto?>;
