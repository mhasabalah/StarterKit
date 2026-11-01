using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Contracts.Subscriptions.Commands;

public record CancelSubscriptionCommand(
    Guid SubscriptionId,
    Guid ActorId,
    bool IsAdminActor,
    string? Reason,
    CancellationReason? CancellationReason = null,
    string? CancellationNote = null) : ICommand;
