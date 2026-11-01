namespace Subscriptions.Contracts.Subscriptions.Events;

public record PaymentRejectedEvent(
    Guid PaymentId,
    Guid SubscriptionId,
    Guid SubscriberId,
    string Reason) : IDomainEvent;
