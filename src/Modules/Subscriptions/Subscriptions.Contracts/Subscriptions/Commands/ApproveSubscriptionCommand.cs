namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <param name="PaymentId">
/// The submitted activation payment being approved. Optional for backward compatibility: when omitted, the single
/// submitted payment is used; when several are submitted the request is rejected (400).
/// </param>
/// <param name="Version">The subscription version the admin saw (optimistic concurrency; stale = 409).</param>
public record ApproveSubscriptionCommand(Guid SubscriptionId, Guid AdminId, Guid? PaymentId = null, long? Version = null)
    : ICommand<ApproveSubscriptionResult>;
