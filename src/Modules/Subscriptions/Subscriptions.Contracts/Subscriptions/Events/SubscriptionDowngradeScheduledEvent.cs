namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionDowngradeScheduledEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid NewPlanId,
    DateTimeOffset EffectiveAt) : IDomainEvent;
