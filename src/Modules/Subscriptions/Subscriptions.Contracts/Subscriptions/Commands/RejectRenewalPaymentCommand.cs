namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <param name="PaymentId">The submitted renewal payment being rejected (optional only when exactly one is submitted).</param>
/// <param name="Version">The subscription version the admin saw.</param>
public record RejectRenewalPaymentCommand(
    Guid SubscriptionId,
    Guid? PaymentId,
    Guid AdminId,
    string Reason,
    long? Version = null) : ICommand;
