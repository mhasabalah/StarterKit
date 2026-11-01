namespace Subscriptions.Contracts.Subscriptions.Events;

public record RefundRequestedEvent(
    Guid RefundRequestId,
    Guid SubscriptionId,
    Guid SubscriberId,
    decimal RequestedAmount,
    string Currency) : IDomainEvent;
