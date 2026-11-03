namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <param name="PaymentId">The submitted payment being rejected (optional only when exactly one is submitted).</param>
/// <param name="Version">The subscription version the admin saw.</param>
public record RejectSubscriptionCommand(
    Guid SubscriptionId,
    Guid AdminId,
    string? Reason,
    Guid? PaymentId = null,
    long? Version = null) : ICommand;
