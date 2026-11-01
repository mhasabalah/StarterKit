using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessScheduledDowngradesJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessScheduledDowngradesJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var now = DateTimeOffset.UtcNow;

        var pendingSubscriptionIds = await dbContext.ScheduledPlanChanges
            .AsNoTracking()
            .Where(x => x.Status == PlanChangeStatus.Pending && x.EffectiveAt <= now)
            .Select(x => x.SubscriptionId)
            .ToListAsync(CancellationToken.None);

        foreach (var subscriptionId in pendingSubscriptionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var subscription = await dbContext.Subscriptions
                    .Include(x => x.ScheduledPlanChange)
                    .FirstOrDefaultAsync(x => x.Id == subscriptionId, CancellationToken.None);

                if (subscription?.ScheduledPlanChange is not { Status: PlanChangeStatus.Pending } change
                    || change.EffectiveAt > now)
                    continue;

                subscription.ApplyScheduledDowngrade();

                await dbContext.SaveChangesAsync(CancellationToken.None);
                await cache.RemoveAsync(
                    SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId),
                    CancellationToken.None);

                logger.LogInformation(
                    "Applied scheduled downgrade for subscription {SubscriptionId} subscriber {SubscriberId}.",
                    subscription.Id, subscription.SubscriberId);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "Concurrency conflict applying scheduled downgrade for subscription {SubscriptionId}.",
                    subscriptionId);
                dbContext.ChangeTracker.Clear();
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error applying scheduled downgrade for subscription {SubscriptionId}.",
                    subscriptionId);
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
