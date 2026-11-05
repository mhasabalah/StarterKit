namespace Subscriptions.Data.Repositories;

/// <summary>
/// Loads subscriptions for command handlers. Never loads payments and invoices together (no cartesian explosion):
/// the subscription comes with its PENDING plan change only; payments are loaded explicitly when needed.
/// </summary>
public interface ISubscriptionRepository
{
    /// <summary>Tracked subscription + its pending scheduled plan change (if any).</summary>
    Task<Subscription?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Tracked payment of the subscription (fixes up into <c>Subscription.Payments</c> when that is tracked).</summary>
    Task<SubscriptionPayment?> GetPaymentAsync(Guid subscriptionId, Guid paymentId, CancellationToken ct = default);

    /// <summary>Tracked payments still owed (Pending / Submitted / Rejected / PartiallyPaid).</summary>
    Task<IReadOnlyList<SubscriptionPayment>> GetAwaitingPaymentsAsync(Guid subscriptionId, CancellationToken ct = default);

    /// <summary>The subscriber's open (PendingApproval / Active / PastDue) subscription, tracked.</summary>
    Task<Subscription?> GetOpenBySubscriberIdAsync(Guid subscriberId, CancellationToken ct = default);

    Task<bool> HasOpenSubscriptionAsync(Guid subscriberId, CancellationToken ct = default);

    /// <summary>True when the subscriber ever started a free trial (any status): one trial per subscriber.</summary>
    Task<bool> HasUsedTrialAsync(Guid subscriberId, CancellationToken ct = default);

    /// <summary>Another payment whose proof has the same SHA-256 content hash (oldest first), or null.</summary>
    Task<Guid?> FindPaymentWithProofHashAsync(string contentHash, Guid excludingPaymentId, CancellationToken ct = default);

    Task AddAsync(Subscription subscription, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
