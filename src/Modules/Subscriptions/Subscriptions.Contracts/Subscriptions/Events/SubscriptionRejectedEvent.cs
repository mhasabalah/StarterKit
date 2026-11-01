namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionRejectedEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    string? Reason) : IDomainEvent;
