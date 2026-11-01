namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionPastDueEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    DateTimeOffset GracePeriodEndsAt,
    Guid NewPaymentId) : IDomainEvent;
