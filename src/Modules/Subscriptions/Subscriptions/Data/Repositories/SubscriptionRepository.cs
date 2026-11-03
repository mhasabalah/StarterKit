namespace Subscriptions.Data.Repositories;

public class SubscriptionRepository(SubscriptionsDbContext context)
    : ISubscriptionRepository
{
    public async Task<Subscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Subscriptions
            .Include(x => x.ScheduledPlanChanges.Where(c => c.Status == PlanChangeStatus.Pending))
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<SubscriptionPayment?> GetPaymentAsync(Guid subscriptionId, Guid paymentId, CancellationToken ct = default)
        => await context.Payments
            .FirstOrDefaultAsync(x => x.Id == paymentId && x.SubscriptionId == subscriptionId, ct);

    public async Task<IReadOnlyList<SubscriptionPayment>> GetAwaitingPaymentsAsync(Guid subscriptionId, CancellationToken ct = default)
        => await context.Payments
            .Where(x => x.SubscriptionId == subscriptionId &&
                        (x.Status == PaymentStatus.Pending ||
                         x.Status == PaymentStatus.Submitted ||
                         x.Status == PaymentStatus.Rejected ||
                         x.Status == PaymentStatus.PartiallyPaid))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task<Subscription?> GetOpenBySubscriberIdAsync(Guid subscriberId, CancellationToken ct = default)
        => await context.Subscriptions
            .Include(x => x.ScheduledPlanChanges.Where(c => c.Status == PlanChangeStatus.Pending))
            .FirstOrDefaultAsync(x =>
                    x.SubscriberId == subscriberId &&
                    (x.Status == SubscriptionStatus.PendingApproval ||
                     x.Status == SubscriptionStatus.Active ||
                     x.Status == SubscriptionStatus.PastDue),
                ct);

    public async Task<bool> HasOpenSubscriptionAsync(Guid subscriberId, CancellationToken ct = default)
        => await context.Subscriptions.AnyAsync(x =>
                x.SubscriberId == subscriberId &&
                (x.Status == SubscriptionStatus.PendingApproval ||
                 x.Status == SubscriptionStatus.Active ||
                 x.Status == SubscriptionStatus.PastDue),
            ct);

    public async Task<Guid?> FindPaymentWithProofHashAsync(string contentHash, Guid excludingPaymentId, CancellationToken ct = default)
        => await context.Payments
            .AsNoTracking()
            .Where(x => x.ProofContentHash == contentHash && x.Id != excludingPaymentId)
            .OrderBy(x => x.SubmittedAt)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(Subscription subscription, CancellationToken ct = default)
        => await context.Subscriptions.AddAsync(subscription, ct);

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await context.SaveChangesAsync(ct);
}
