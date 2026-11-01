namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionUpgradedEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid OldPlanId,
    Guid NewPlanId,
    decimal ProratedAmount,
    bool IsDeferred) : IDomainEvent;
