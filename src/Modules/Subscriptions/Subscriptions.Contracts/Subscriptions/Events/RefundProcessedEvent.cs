namespace Subscriptions.Contracts.Subscriptions.Events;

public record RefundProcessedEvent(
    Guid RefundRequestId,
    Guid SubscriptionId,
    Guid SubscriberId,
    decimal Amount,
    string Currency,
    string ExternalTransactionRef) : IDomainEvent;
