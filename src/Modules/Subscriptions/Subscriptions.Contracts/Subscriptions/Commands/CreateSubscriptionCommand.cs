namespace Subscriptions.Contracts.Subscriptions.Commands;

public record CreateSubscriptionCommand(Guid SubscriberId, Guid PlanId)
    : ICommand<CreateSubscriptionResult>;
