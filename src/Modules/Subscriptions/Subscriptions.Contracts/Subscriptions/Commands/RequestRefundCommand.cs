namespace Subscriptions.Contracts.Subscriptions.Commands;

public record RequestRefundCommand(
    Guid SubscriptionId,
    Guid SubscriberId) : ICommand<Guid>;
