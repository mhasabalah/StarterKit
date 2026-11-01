using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Contracts.Subscriptions.Events;

public record SubscriptionCancelledEvent(
    Guid SubscriptionId,
    Guid SubscriberId,
    DateTimeOffset CancelledAt,
    Guid ActorId,
    CancellationReason? CancellationReason = null,
    bool IsScheduled = false) : IDomainEvent;
