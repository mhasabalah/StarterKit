namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// Admin rejects the submitted proof of an upgrade proration payment. The subscriber may upload a new proof
/// until the upgrade SLA elapses; after that the unpaid upgrade is reverted.
/// </summary>
/// <param name="Version">The PAYMENT version the admin saw.</param>
public record RejectProrationPaymentCommand(
    Guid SubscriptionId,
    Guid PaymentId,
    Guid AdminId,
    string Reason,
    long? Version = null) : ICommand;
