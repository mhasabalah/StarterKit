using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

/// <summary>Repository of <see cref="PaymentAttempt"/> rows (automatic charge attempts, one per payment + step).</summary>
public interface IPaymentAttemptRepository
{
    /// <summary>Tracked attempt for a payment + dunning step, or null.</summary>
    Task<PaymentAttempt?> GetAsync(Guid paymentId, string step, CancellationToken ct = default);

    void Add(PaymentAttempt attempt);

    Task SaveChangesAsync(CancellationToken ct = default);
}
