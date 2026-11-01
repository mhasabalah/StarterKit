namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionDowngradeCancelledEvent(
    Guid SubscriptionId,
    Guid SubscriberId) : IDomainEvent;
