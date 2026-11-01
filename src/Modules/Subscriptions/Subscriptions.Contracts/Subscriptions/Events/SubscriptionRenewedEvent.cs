namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionRenewedEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    DateTimeOffset NewRenewalDate) : IDomainEvent;
