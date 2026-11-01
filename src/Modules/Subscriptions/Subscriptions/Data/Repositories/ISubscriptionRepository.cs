namespace Subscriptions.Data.Repositories;

public interface ISubscriptionRepository
{
    Task<Subscription?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Subscription?> GetByIdForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<Subscription?> GetActiveBySubscriberIdAsync(
        Guid subscriberId,
        CancellationToken ct = default);
    Task<bool> HasActiveSubscriptionAsync(Guid subscriberId, CancellationToken ct = default);
    Task AddAsync(Subscription sub, CancellationToken ct = default);
    Task UpdateAsync(Subscription sub, CancellationToken ct = default);
    Task AddInvoiceAsync(SubscriptionInvoice invoice, CancellationToken ct = default);
    Task AddScheduledPlanChangeAsync(ScheduledPlanChange change, CancellationToken ct = default);
    Task AddTransitionLogAsync(SubscriptionTransitionLog log, CancellationToken ct = default);
    Task AddAuditLogAsync(AdminActionAuditLog log, CancellationToken ct = default);
}
