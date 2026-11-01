namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionActivatedEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    DateTimeOffset StartDate,
    DateTimeOffset RenewalDate) : IDomainEvent;
