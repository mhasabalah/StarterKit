using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessUpgradeSlaExpiryJob(
    IServiceScopeFactory scopeFactory,
    IOptions<SubscriptionOptions> options,
    ILogger<ProcessUpgradeSlaExpiryJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var planRepository = scope.ServiceProvider.GetRequiredService<IPlanRepository>();

        var slaHours = options.Value.UpgradeSlaHours;
        var cutoff = DateTimeOffset.UtcNow.AddHours(-slaHours);

        // Find subscriptions with non-deferred proration invoices older than SLA window
        // where the associated payment has not been approved (Paid). Capture OriginalPlanId
        // from the invoice so we can revert to the correct pre-upgrade plan.
        var expiredUpgrades = await dbContext.Invoices
            .AsNoTracking()
            .Where(inv =>
                inv.Type == InvoiceType.Proration &&
                !inv.IsDeferred &&
                !inv.IsVoided &&
                inv.OriginalPlanId != null &&
                inv.IssuedAt <= cutoff)
            .Join(dbContext.Subscriptions,
                inv => inv.SubscriptionId,
                sub => sub.Id,
                (inv, sub) => new { InvoiceId = inv.Id, SubscriptionId = sub.Id, inv.PaymentId, inv.OriginalPlanId })
            .Where(x => x.PaymentId != null)
            .Join(dbContext.Payments,
                x => x.PaymentId,
                p => p.Id,
                (x, p) => new { x.SubscriptionId, x.OriginalPlanId, p.Status })
            .Where(x => x.Status != PaymentStatus.Paid)
            .Select(x => new { x.SubscriptionId, x.OriginalPlanId })
            .Distinct()
            .ToListAsync(CancellationToken.None);

        foreach (var item in expiredUpgrades)
        {
            var subscriptionId = item.SubscriptionId;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sub = await dbContext.Subscriptions
                    .Include(x => x.Invoices)
                    .FirstOrDefaultAsync(x => x.Id == subscriptionId, CancellationToken.None);

                if (sub is null || sub.Status != SubscriptionStatus.Active)
                    continue;

                var originalPlan = await planRepository.GetByIdAsync(item.OriginalPlanId!.Value, CancellationToken.None);
                if (originalPlan is null)
                {
                    logger.LogWarning(
                        "Cannot revert subscription {SubscriptionId}: original plan {PlanId} not found.",
                        subscriptionId, item.OriginalPlanId);
                    continue;
                }

                var originalFeaturesJson = System.Text.Json.JsonSerializer.Serialize(
                    originalPlan.Features.ToDictionary(x => x.Key, _ => "true"));

                sub.RevertUpgrade(originalPlan, originalFeaturesJson, "Upgrade SLA expired without proof submission");

                // Void the proration invoices that weren't paid
                foreach (var invoice in sub.Invoices.Where(i =>
                    i.Type == InvoiceType.Proration && !i.IsVoided && !i.IsDeferred))
                {
                    invoice.Void();
                }

                await dbContext.SaveChangesAsync(CancellationToken.None);
                await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(sub.SubscriberId));

                logger.LogInformation(
                    "Reverted subscription {SubscriptionId} upgrade for subscriber {SubscriberId} due to SLA expiry.",
                    sub.Id, sub.SubscriberId);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "Concurrency conflict reverting subscription {SubscriptionId}.",
                    subscriptionId);
                dbContext.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error reverting subscription {SubscriptionId} upgrade.",
                    subscriptionId);
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
