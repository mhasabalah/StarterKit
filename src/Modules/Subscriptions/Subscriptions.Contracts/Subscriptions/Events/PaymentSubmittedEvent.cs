namespace Subscriptions.Contracts.Subscriptions.Events;

public record PaymentSubmittedEvent(
    Guid PaymentId,
    Guid SubscriptionId,
    Guid SubscriberId) : IDomainEvent;
