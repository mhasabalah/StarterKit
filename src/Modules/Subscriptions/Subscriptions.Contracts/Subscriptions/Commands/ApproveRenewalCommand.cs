namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <param name="PaymentId">The submitted renewal payment being approved (optional only when exactly one is submitted).</param>
/// <param name="Version">The subscription version the admin saw (optimistic concurrency; stale = 409).</param>
public record ApproveRenewalCommand(Guid SubscriptionId, Guid AdminId, Guid? PaymentId = null, long? Version = null)
    : ICommand<ApproveRenewalResult>;
