namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>
/// Hourly job (<c>process-plan-price-changes</c>): applies scheduled plan price changes whose effective date passed.
/// Each plan is its own <see cref="IUnitOfWorkRunner"/> unit of work (fresh change tracker, events published after
/// its commit); one failing plan is logged and skipped.
/// </summary>
[DisableConcurrentExecution(60)]
public class ProcessPlanPriceChangesJob(
    IUnitOfWorkRunner unitOfWork,
    TimeProvider timeProvider,
    ILogger<ProcessPlanPriceChangesJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = timeProvider.GetUtcNow();
        var planIds = await unitOfWork.RunAsync((services, ct) =>
            services.GetRequiredService<SubscriptionsDbContext>().Plans
                .AsNoTracking()
                .Where(p => p.PendingPrice != null
                            && p.PriceEffectiveAt != null
                            && p.PriceEffectiveAt <= now
                            && !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync(ct), CancellationToken.None);

        foreach (var planId in planIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await unitOfWork.RunAsync((services, ct) => ApplyAsync(services, planId, now, ct), CancellationToken.None);
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict())
            {
                logger.LogWarning(ex, "Price change for plan {PlanId} skipped: modified concurrently; the next run retries.", planId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error applying price change for plan {PlanId}.", planId);
            }
        }
    }

    private async Task ApplyAsync(IServiceProvider services, Guid planId, DateTimeOffset now, CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var plan = await dbContext.Plans.FirstOrDefaultAsync(p => p.Id == planId, ct);

        if (plan is null || plan.PendingPrice is null
            || plan.PriceEffectiveAt is null || plan.PriceEffectiveAt > now)
            return;

        var oldPrice = plan.Price;
        var newPrice = plan.PendingPrice.Value;

        plan.ApplyPriceChange();
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation(
            "Applied price change for plan {PlanId} ({PlanName}): {OldPrice} → {NewPrice} {Currency}",
            plan.Id, plan.Name.En, oldPrice, newPrice, plan.Currency);
    }
}
