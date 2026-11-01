namespace Subscriptions.Contracts.Subscriptions.Events;

public record PaymentApprovedEvent(
    Guid PaymentId,
    Guid SubscriptionId,
    Guid SubscriberId) : IDomainEvent;
