namespace Subscriptions.Contracts.Subscriptions.Events;

public record RefundApprovedEvent(
    Guid RefundRequestId,
    Guid SubscriptionId,
    Guid SubscriberId,
    decimal ApprovedAmount,
    string Currency) : IDomainEvent;
