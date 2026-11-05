using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Auditing;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Catalog.Domains.Products.Jobs;

/// <summary>
/// Hangfire recurring job (scheduled in UseCatalogModule): removes Catalog audit entries older than
/// <see cref="AuditTrailOptions.RetentionDays"/> with set-based deletes (the audit table is append-only for the change
/// tracker). Runs once per tenant (<see cref="ITenantJobRunner"/>), so each tenant's database / filters / RLS apply.
/// </summary>
[DisableConcurrentExecution(60)]
public class PurgeAuditEntriesJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    TimeProvider timeProvider,
    IOptions<AuditTrailOptions> options,
    ILogger<PurgeAuditEntriesJob> logger)
{
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await PurgeCurrentTenantAsync(cancellationToken),
            nameof(PurgeAuditEntriesJob));
        return total;
    }

    /// <summary>Purges the CURRENT ambient tenant's expired audit entries.</summary>
    public async Task<int> PurgeCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        var settings = options.Value;
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-settings.RetentionDays);
        var batch = Math.Max(1, settings.PurgeBatchSize);

        var total = 0;
        int removed;
        do
        {
            cancellationToken?.ThrowIfCancellationRequested();
            removed = await unitOfWork.RunAsync(
                (services, ct) => services.GetRequiredService<CatalogDbContext>().PurgeAuditEntriesAsync(cutoff, batch, ct),
                CancellationToken.None);
            total += removed;
        } while (removed >= batch);

        logger.LogInformation("PurgeAuditEntriesJob: removed {Removed} audit entries older than {Cutoff}", total, cutoff);
        return total;
    }
}
