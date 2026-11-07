using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

public class PaymentAttemptRepository(SubscriptionsDbContext context) : IPaymentAttemptRepository
{
    public Task<PaymentAttempt?> GetAsync(Guid paymentId, string step, CancellationToken ct = default)
        => context.PaymentAttempts.FirstOrDefaultAsync(x => x.PaymentId == paymentId && x.Step == step, ct);

    public async Task<IReadOnlyList<PaymentAttempt>> GetForPaymentAsync(Guid paymentId, CancellationToken ct = default)
        => await context.PaymentAttempts.Where(x => x.PaymentId == paymentId).OrderBy(x => x.CreatedAt).ToListAsync(ct);

    public void Add(PaymentAttempt attempt) => context.PaymentAttempts.Add(attempt);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
