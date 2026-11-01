namespace Subscriptions.Data.Repositories;

public class SubscriptionRepository(SubscriptionsDbContext context)
    : ISubscriptionRepository
{
    public async Task<Subscription?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Subscriptions
            .Include(x => x.Payments)
            .Include(x => x.Invoices)
            .Include(x => x.ScheduledPlanChange)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Subscription?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default)
        => await context.Subscriptions
            .Include(x => x.ScheduledPlanChange)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Subscription?> GetActiveBySubscriberIdAsync(
        Guid subscriberId,
        CancellationToken ct = default)
        => await context.Subscriptions
            .Include(x => x.Payments)
            .Include(x => x.Invoices)
            .Include(x => x.ScheduledPlanChange)
            .FirstOrDefaultAsync(x =>
                    x.SubscriberId == subscriberId &&
                    (x.Status == SubscriptionStatus.PendingApproval ||
                     x.Status == SubscriptionStatus.Active ||
                     x.Status == SubscriptionStatus.PastDue),
                ct);

    public async Task<bool> HasActiveSubscriptionAsync(Guid subscriberId, CancellationToken ct = default)
        => await context.Subscriptions.AnyAsync(x =>
                x.SubscriberId == subscriberId &&
                (x.Status == SubscriptionStatus.PendingApproval ||
                 x.Status == SubscriptionStatus.Active ||
                 x.Status == SubscriptionStatus.PastDue),
            ct);

    public async Task AddAsync(Subscription sub, CancellationToken ct = default)
    {
        await context.Subscriptions.AddAsync(sub, ct);
    }

    public async Task UpdateAsync(Subscription sub, CancellationToken ct = default)
    {
        context.Subscriptions.Update(sub);
        await context.SaveChangesAsync(ct);
    }

    public async Task AddInvoiceAsync(SubscriptionInvoice invoice, CancellationToken ct = default)
    {
        await context.Invoices.AddAsync(invoice, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task AddScheduledPlanChangeAsync(ScheduledPlanChange change, CancellationToken ct = default)
    {
        await context.ScheduledPlanChanges.AddAsync(change, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task AddTransitionLogAsync(SubscriptionTransitionLog log, CancellationToken ct = default)
    {
        await context.TransitionLogs.AddAsync(log, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task AddAuditLogAsync(AdminActionAuditLog log, CancellationToken ct = default)
    {
        await context.AdminAuditLogs.AddAsync(log, ct);
        await context.SaveChangesAsync(ct);
    }
}
