namespace Subscriptions.Contracts.Subscriptions.Commands;

public record ApproveRenewalCommand(Guid SubscriptionId, Guid AdminId)
    : ICommand<ApproveRenewalResult>;
