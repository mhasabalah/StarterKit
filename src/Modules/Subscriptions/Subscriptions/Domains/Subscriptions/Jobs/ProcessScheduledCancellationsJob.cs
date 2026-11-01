using Hangfire;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessScheduledCancellationsJob(
    IServiceScopeFactory scopeFactory,
    IOptions<SubscriptionOptions> options,
    ILogger<ProcessScheduledCancellationsJob> logger)
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
            .Where(x => x.Status == SubscriptionStatus.Active
                     && x.CancelledAtPeriodEnd
                     && x.ScheduledCancellationDate <= now)
            .Select(x => x.Id)
            .ToListAsync(CancellationToken.None);

        foreach (var subscriptionId in subscriptionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var sub = await dbContext.Subscriptions
                    .Include(x => x.ScheduledPlanChange)
                    .FirstOrDefaultAsync(x => x.Id == subscriptionId, CancellationToken.None);

                if (sub is null || sub.Status != SubscriptionStatus.Active || !sub.CancelledAtPeriodEnd)
                    continue;

                sub.ApplyScheduledCancellation(options.Value.SystemActorId);

                await dbContext.TransitionLogs.AddAsync(
                    SubscriptionTransitionLog.Create(
                        sub.Id,
                        sub.SubscriberId,
                        SubscriptionStatus.Active,
                        SubscriptionStatus.Cancelled,
                        options.Value.SystemActorId,
                        "System",
                        "Scheduled cancellation applied at period end"),
                    CancellationToken.None);

                if (sub.BillingCycleInMonths == 12 && sub.StartDate.HasValue && sub.RenewalDate.HasValue)
                {
                    var proratedWindowEnd = sub.StartDate.Value.AddDays(options.Value.RefundProratedWindowDays);
                    if (now <= proratedWindowEnd)
                    {
                        var fullWindowEnd = sub.StartDate.Value.AddDays(options.Value.RefundFullWindowDays);
                        RefundType refundType;
                        decimal refundAmount;

                        if (now <= fullWindowEnd)
                        {
                            refundType = RefundType.Full;
                            refundAmount = sub.Price;
                        }
                        else
                        {
                            refundType = RefundType.Prorated;
                            var remainingDays = (sub.RenewalDate.Value - now).TotalDays;
                            var totalDays = sub.BillingCycleInMonths * 30m;
                            refundAmount = Math.Round((decimal)(remainingDays / (double)totalDays) * sub.Price, 2);
                        }

                        if (refundAmount > 0)
                        {
                            var refundRequest = SubscriptionRefundRequest.Create(
                                sub.Id,
                                sub.SubscriberId,
                                sub.PlanId,
                                sub.PlanName,
                                sub.BillingCycleInMonths,
                                sub.StartDate.Value,
                                now,
                                sub.RenewalDate.Value,
                                refundAmount,
                                sub.Currency,
                                refundType);

                            await dbContext.RefundRequests.AddAsync(refundRequest, CancellationToken.None);
                        }
                    }
                }

                await dbContext.SaveChangesAsync(CancellationToken.None);
                await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(sub.SubscriberId));

                logger.LogInformation(
                    "Applied scheduled cancellation for subscription {SubscriptionId}, subscriber {SubscriberId}",
                    sub.Id, sub.SubscriberId);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                logger.LogWarning(ex,
                    "Concurrency conflict while applying scheduled cancellation for {SubscriptionId}",
                    subscriptionId);
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
