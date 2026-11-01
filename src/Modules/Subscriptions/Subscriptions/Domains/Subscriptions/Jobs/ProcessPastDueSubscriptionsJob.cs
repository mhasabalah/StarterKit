using Hangfire;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessPastDueSubscriptionsJob(
    IServiceScopeFactory scopeFactory,
    IOptions<SubscriptionOptions> options,
    ILogger<ProcessPastDueSubscriptionsJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();

        var now = DateTimeOffset.UtcNow;
        var subscriptionIds = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.Status == SubscriptionStatus.Active && x.RenewalDate <= now)
            .Select(x => x.Id)
            .ToListAsync(CancellationToken.None);

        foreach (var subscriptionId in subscriptionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sub = await dbContext.Subscriptions
                    .Include(x => x.Payments)
                    .FirstOrDefaultAsync(x => x.Id == subscriptionId, CancellationToken.None);
                if (sub is null || sub.Status != SubscriptionStatus.Active)
                {
                    continue;
                }

                // Resolve the price the user must pay — accounts for a price change that
                // became effective between the subscription's last renewal and now.
                var plan = await dbContext.Plans
                    .AsNoTracking()
                    .Where(p => p.Id == sub.PlanId)
                    .Select(p => new { p.Price, p.PendingPrice, p.PriceEffectiveAt })
                    .FirstOrDefaultAsync(CancellationToken.None);

                decimal effectivePrice;
                if (plan is { PendingPrice: not null, PriceEffectiveAt: not null } && plan.PriceEffectiveAt <= now)
                    effectivePrice = plan.PendingPrice.Value;   // effective but job hasn't promoted yet
                else if (plan != null && plan.Price != sub.Price)
                    effectivePrice = plan.Price;                // already promoted
                else
                    effectivePrice = sub.Price;

                // Keep the subscription's price snapshot in sync so next renewal is also correct.
                if (effectivePrice != sub.Price)
                    sub.UpdatePrice(effectivePrice);

                var newPayment = sub.MarkPastDue(options.Value.GracePeriodDays, effectivePrice);
                await dbContext.TransitionLogs.AddAsync(
                    SubscriptionTransitionLog.Create(
                        sub.Id,
                        sub.SubscriberId,
                        SubscriptionStatus.Active,
                        SubscriptionStatus.PastDue,
                        options.Value.SystemActorId,
                        "System",
                        "Renewal date elapsed"),
                    CancellationToken.None);

                await dbContext.SaveChangesAsync(CancellationToken.None);
                await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(sub.SubscriberId));

                logger.LogInformation(
                    "Processed past due subscription {SubscriptionId} for subscriber {SubscriberId}. Grace ends {GracePeriodEndsAt}. Payment {PaymentId}",
                    sub.Id,
                    sub.SubscriberId,
                    sub.GracePeriodEndsAt,
                    newPayment.Id);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "Subscription {SubscriptionId} was updated by another process while marking past due.",
                    subscriptionId);
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
