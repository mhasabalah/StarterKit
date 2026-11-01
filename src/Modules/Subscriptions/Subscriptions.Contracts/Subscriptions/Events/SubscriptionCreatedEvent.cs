namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionCreatedEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid PlanId,
    string PlanName) : IDomainEvent;
