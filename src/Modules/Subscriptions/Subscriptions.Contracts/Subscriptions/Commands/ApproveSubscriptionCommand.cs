namespace Subscriptions.Contracts.Subscriptions.Commands;

public record ApproveSubscriptionCommand(Guid SubscriptionId, Guid AdminId)
    : ICommand<ApproveSubscriptionResult>;
