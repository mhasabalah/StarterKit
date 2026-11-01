namespace Subscriptions.Contracts.Subscriptions.Events;

public record InvoiceGenerationFailedEvent(
    Guid InvoiceId,
    Guid SubscriptionId,
    Guid SubscriberId,
    string ErrorMessage,
    int RetryCount) : IDomainEvent;
