namespace Subscriptions.Contracts.Subscriptions.Events;

public record InvoiceGeneratedEvent(
    Guid InvoiceId,
    Guid SubscriptionId,
    Guid SubscriberId,
    string InvoiceType,
    decimal Amount) : IDomainEvent;
