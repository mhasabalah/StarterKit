namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionUpgradeRevertedEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid RevertedToPlanId) : IDomainEvent;
