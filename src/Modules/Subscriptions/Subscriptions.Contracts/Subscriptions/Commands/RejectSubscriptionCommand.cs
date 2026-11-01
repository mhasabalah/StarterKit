namespace Subscriptions.Contracts.Subscriptions.Commands;

public record RejectSubscriptionCommand(Guid SubscriptionId, Guid AdminId, string? Reason) : ICommand;
