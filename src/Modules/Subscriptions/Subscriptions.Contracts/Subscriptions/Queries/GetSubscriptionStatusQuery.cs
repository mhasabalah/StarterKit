using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetSubscriptionStatusQuery(Guid SubscriberId) : IQuery<SubscriptionStatusDto?>;
