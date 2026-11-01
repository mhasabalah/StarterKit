using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionCancellationScheduledEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid ActorId,
    DateTimeOffset ScheduledCancellationDate,
    CancellationReason? CancellationReason) : IDomainEvent;
