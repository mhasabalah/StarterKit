using Hangfire;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessExpiredSubscriptionsJob(
    IServiceScopeFactory scopeFactory,
    IOptions<SubscriptionOptions> options,
    ILogger<ProcessExpiredSubscriptionsJob> logger)
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
            .Where(x => x.Status == SubscriptionStatus.PastDue && x.GracePeriodEndsAt <= now)
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
                if (sub is null || sub.Status != SubscriptionStatus.PastDue)
                {
                    continue;
                }

                sub.Expire();
                var latestPayment = sub.Payments.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
                latestPayment?.MarkFailed();

                await dbContext.TransitionLogs.AddAsync(
                    SubscriptionTransitionLog.Create(
                        sub.Id,
                        sub.SubscriberId,
                        SubscriptionStatus.PastDue,
                        SubscriptionStatus.Expired,
                        options.Value.SystemActorId,
                        "System",
                        "Grace period elapsed"),
                    CancellationToken.None);

                await dbContext.SaveChangesAsync(CancellationToken.None);
                await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(sub.SubscriberId));

                logger.LogInformation(
                    "Expired subscription {SubscriptionId} for subscriber {SubscriberId} at {ExpiredAt}",
                    sub.Id,
                    sub.SubscriberId,
                    now);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "Subscription {SubscriptionId} was updated by another process while expiring.",
                    subscriptionId);
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
