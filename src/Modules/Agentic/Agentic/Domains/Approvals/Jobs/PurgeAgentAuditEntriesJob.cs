using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Auditing;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Agentic.Domains.Approvals.Jobs;

/// <summary>
/// Recurring, per tenant: removes Agentic audit entries (approval decisions, policy changes) older than
/// <see cref="AuditTrailOptions.RetentionDays"/> with set-based deletes (the audit table is append-only for the change
/// tracker). Each tenant's database, filters and RLS apply.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class PurgeAgentAuditEntriesJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    TimeProvider timeProvider,
    IOptions<AuditTrailOptions> options,
    ILogger<PurgeAgentAuditEntriesJob> logger)
{
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await PurgeCurrentTenantAsync(cancellationToken),
            nameof(PurgeAgentAuditEntriesJob));
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
                (services, ct) => services.GetRequiredService<AgenticDbContext>().PurgeAuditEntriesAsync(cutoff, batch, ct),
                CancellationToken.None);
            total += removed;
        } while (removed >= batch);

        logger.LogInformation("PurgeAgentAuditEntriesJob: removed {Removed} audit entries older than {Cutoff}", total, cutoff);
        return total;
    }
}
