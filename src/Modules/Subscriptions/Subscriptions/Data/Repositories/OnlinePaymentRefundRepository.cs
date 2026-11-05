using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

/// <summary>
/// Refunds the provider gave back for online sessions. Reads and adds participate in the caller's unit of work (the
/// caller saves); nothing here commits by itself.
/// </summary>
public interface IOnlinePaymentRefundRepository
{
    /// <summary>Tracked refund for the provider's refund id, or null.</summary>
    Task<OnlinePaymentRefund?> FindAsync(string provider, string providerRefundId, CancellationToken ct = default);

    /// <summary>Σ of the refunds booked against a session.</summary>
    Task<decimal> SumForSessionAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Σ of the refunds booked for a request.</summary>
    Task<decimal> SumForRequestAsync(Guid refundRequestId, CancellationToken ct = default);

    /// <summary>Σ of the refunds of a request that the provider's webhook already booked in the ledger.</summary>
    Task<decimal> SumBookedByWebhookForRequestAsync(Guid refundRequestId, CancellationToken ct = default);

    Task<List<string>> GetProviderRefundIdsForRequestAsync(Guid refundRequestId, CancellationToken ct = default);

    /// <summary>
    /// Tracked refunds of the subscription that the provider's webhook booked on its own (no refund request) at or after
    /// <paramref name="since"/>, oldest first: candidates an interrupted admin refund adopts instead of refunding again.
    /// </summary>
    Task<List<OnlinePaymentRefund>> GetUnlinkedWebhookRefundsSinceAsync(
        Guid subscriptionId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Settled sessions of a subscription, newest first.</summary>
    Task<List<OnlinePaymentSession>> GetSettledSessionsAsync(Guid subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// Row-locks the session until the transaction ends, so two concurrent refunds against it cannot both pass the
    /// "never more than received" cap. Needs an open transaction.
    /// </summary>
    Task LockSessionAsync(Guid sessionId, CancellationToken ct = default);

    void Add(OnlinePaymentRefund refund);
}

public sealed class OnlinePaymentRefundRepository(SubscriptionsDbContext context) : IOnlinePaymentRefundRepository
{
    public Task<OnlinePaymentRefund?> FindAsync(string provider, string providerRefundId, CancellationToken ct = default)
        => context.OnlinePaymentRefunds.FirstOrDefaultAsync(
            x => x.Provider == provider && x.ProviderRefundId == providerRefundId, ct);

    public async Task<decimal> SumForSessionAsync(Guid sessionId, CancellationToken ct = default)
        => await context.OnlinePaymentRefunds.AsNoTracking()
            .Where(x => x.SessionId == sessionId).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

    public async Task<decimal> SumForRequestAsync(Guid refundRequestId, CancellationToken ct = default)
        => await context.OnlinePaymentRefunds.AsNoTracking()
            .Where(x => x.RefundRequestId == refundRequestId).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

    public async Task<decimal> SumBookedByWebhookForRequestAsync(Guid refundRequestId, CancellationToken ct = default)
        => await context.OnlinePaymentRefunds.AsNoTracking()
            .Where(x => x.RefundRequestId == refundRequestId && x.BookedByWebhook)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;

    public Task<List<string>> GetProviderRefundIdsForRequestAsync(Guid refundRequestId, CancellationToken ct = default)
        => context.OnlinePaymentRefunds.AsNoTracking()
            .Where(x => x.RefundRequestId == refundRequestId)
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.ProviderRefundId)
            .ToListAsync(ct);

    public Task<List<OnlinePaymentRefund>> GetUnlinkedWebhookRefundsSinceAsync(
        Guid subscriptionId, DateTimeOffset since, CancellationToken ct = default)
        => context.OnlinePaymentRefunds
            .Where(x => x.SubscriptionId == subscriptionId && x.RefundRequestId == null && x.BookedByWebhook &&
                        x.CreatedAt >= since)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public Task<List<OnlinePaymentSession>> GetSettledSessionsAsync(Guid subscriptionId, CancellationToken ct = default)
        => context.OnlinePaymentSessions
            .Where(x => x.SubscriptionId == subscriptionId &&
                        (x.Status == OnlinePaymentSessionStatus.Succeeded || x.Status == OnlinePaymentSessionStatus.Refunded))
            .OrderByDescending(x => x.CompletedAt)
            .ToListAsync(ct);

    public Task LockSessionAsync(Guid sessionId, CancellationToken ct = default)
        => context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM subscriptions.online_payment_sessions WHERE id = {sessionId} FOR UPDATE", ct);

    public void Add(OnlinePaymentRefund refund) => context.OnlinePaymentRefunds.Add(refund);
}
