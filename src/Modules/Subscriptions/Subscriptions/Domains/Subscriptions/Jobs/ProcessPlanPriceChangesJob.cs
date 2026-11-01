namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessPlanPriceChangesJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessPlanPriceChangesJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();

        var now = DateTimeOffset.UtcNow;
        var planIds = await dbContext.Plans
            .AsNoTracking()
            .Where(p => p.PendingPrice != null
                        && p.PriceEffectiveAt != null
                        && p.PriceEffectiveAt <= now
                        && !p.IsDeleted)
            .Select(p => p.Id)
            .ToListAsync(CancellationToken.None);

        foreach (var planId in planIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var plan = await dbContext.Plans
                    .FirstOrDefaultAsync(p => p.Id == planId, CancellationToken.None);

                if (plan is null || plan.PendingPrice is null
                    || plan.PriceEffectiveAt is null || plan.PriceEffectiveAt > now)
                    continue;

                var oldPrice = plan.Price;
                var newPrice = plan.PendingPrice.Value;

                plan.ApplyPriceChange();
                await dbContext.SaveChangesAsync(CancellationToken.None);

                logger.LogInformation(
                    "Applied price change for plan {PlanId} ({PlanName}): {OldPrice} → {NewPrice} {Currency}",
                    plan.Id, plan.Name.En, oldPrice, newPrice, plan.Currency);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error applying price change for plan {PlanId}.", planId);
            }
        }
    }
}
