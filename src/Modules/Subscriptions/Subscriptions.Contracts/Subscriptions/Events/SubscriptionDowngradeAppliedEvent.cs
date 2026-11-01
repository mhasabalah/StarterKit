namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionDowngradeAppliedEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid NewPlanId) : IDomainEvent;
