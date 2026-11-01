using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Commands;

public record CancelScheduledDowngradeCommand(
    Guid SubscriptionId,
    Guid SubscriberId) : ICommand<Unit>;
