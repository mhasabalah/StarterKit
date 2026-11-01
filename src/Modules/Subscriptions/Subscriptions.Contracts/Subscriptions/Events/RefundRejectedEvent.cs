namespace Subscriptions.Contracts.Subscriptions.Events;

public record RefundRejectedEvent(
    Guid RefundRequestId,
    Guid SubscriptionId,
    Guid SubscriberId,
    string Reason) : IDomainEvent;
