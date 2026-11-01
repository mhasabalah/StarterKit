namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionExpiredEvent(
    Guid SubscriptionId,
    Guid SubscriberId) : IDomainEvent;
