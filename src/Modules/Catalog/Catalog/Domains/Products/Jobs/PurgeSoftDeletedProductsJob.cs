using Shared.Core.MultiTenancy.BackgroundJobs;
using Shared.Core.MultiTenancy.Data;
using Catalog.Options;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Catalog.Domains.Products.Jobs;

/// <summary>
/// Hangfire recurring job (scheduled in UseCatalogModule): hard-deletes products that have been
/// soft-deleted for longer than <see cref="CatalogOptions.SoftDeleteRetentionDays"/>. Each product is purged
/// through <see cref="PurgeProductCommand"/> in its own <see cref="IUnitOfWorkRunner"/> unit of work, so
/// ProductPurgedEvent (attachment cleanup) is published after that product's commit and one failure never
/// poisons the change tracker or the events of the next product. Multi-tenancy: runs once per tenant
/// (<see cref="ITenantJobRunner"/>), so each tenant's database/filters/RLS apply and one tenant cannot stop another.
/// </summary>
[DisableConcurrentExecution(60)]
public class PurgeSoftDeletedProductsJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    TimeProvider timeProvider,
    IOptions<CatalogOptions> options,
    ILogger<PurgeSoftDeletedProductsJob> logger)
{
    /// <param name="cancellationToken">Supplied by Hangfire; <c>JobCancellationToken.Null</c> (null) when scheduled.</param>
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await PurgeCurrentTenantAsync(cancellationToken),
            nameof(PurgeSoftDeletedProductsJob));
        return total;
    }

    /// <summary>Purges the CURRENT ambient tenant's expired soft-deleted products.</summary>
    public async Task<int> PurgeCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        cancellationToken?.ThrowIfCancellationRequested();

        var settings = options.Value;
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-settings.SoftDeleteRetentionDays);

        var ids = await unitOfWork.RunAsync((services, ct) =>
            services.GetRequiredService<CatalogDbContext>().Products
                .IgnoreNonIsolationFilters()
                .AsNoTracking()
                .Where(p => p.IsDeleted && p.DeletedAt != null && p.DeletedAt <= cutoff)
                .OrderBy(p => p.DeletedAt)
                .Select(p => p.Id)
                .Take(settings.PurgeBatchSize)
                .ToListAsync(ct), CancellationToken.None);

        var purged = 0;
        foreach (var id in ids)
        {
            cancellationToken?.ThrowIfCancellationRequested();
            try
            {
                var result = await unitOfWork.RunAsync(
                    (services, ct) => services.GetRequiredService<ISender>().Send(new PurgeProductCommand(id), ct),
                    CancellationToken.None);
                if (result.Purged)
                    purged++;
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict())
            {
                logger.LogWarning(ex, "Purging soft-deleted product {ProductId} skipped: modified concurrently", id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Purging soft-deleted product {ProductId} failed", id);
            }
        }

        logger.LogInformation("PurgeSoftDeletedProductsJob: purged {Purged} of {Found} product(s) deleted before {Cutoff}",
            purged, ids.Count, cutoff);
        return purged;
    }
}
