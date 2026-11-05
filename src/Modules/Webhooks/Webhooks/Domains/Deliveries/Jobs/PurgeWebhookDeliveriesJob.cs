using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Webhooks.Domains.Deliveries.Jobs;

/// <summary>
/// Retention (recurring, nightly): deletes finished deliveries (succeeded, failed, cancelled) older than
/// <see cref="WebhooksOptions.RetentionDays"/>, in batches, once per tenant. Pending deliveries are never purged.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class PurgeWebhookDeliveriesJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    TimeProvider timeProvider,
    IOptions<WebhooksOptions> options,
    ILogger<PurgeWebhookDeliveriesJob> logger)
{
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await PurgeCurrentTenantAsync(cancellationToken),
            nameof(PurgeWebhookDeliveriesJob));
        return total;
    }

    /// <summary>Purges the CURRENT ambient tenant's expired deliveries; returns how many were deleted.</summary>
    public async Task<int> PurgeCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        var settings = options.Value;
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-settings.RetentionDays);
        var purged = 0;

        while (true)
        {
            cancellationToken?.ThrowIfCancellationRequested();

            var deleted = await unitOfWork.RunAsync(async (services, ct) =>
            {
                var db = services.GetRequiredService<WebhooksDbContext>();
                var ids = await db.Deliveries.AsNoTracking()
                    .Where(d => d.Status != WebhookDeliveryStatus.Pending && d.CreatedAt != null && d.CreatedAt < cutoff)
                    .OrderBy(d => d.CreatedAt)
                    .Select(d => d.Id)
                    .Take(settings.PurgeBatchSize)
                    .ToListAsync(ct);
                if (ids.Count == 0) return 0;

                return await db.Deliveries.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(ct);
            }, CancellationToken.None);

            purged += deleted;
            if (deleted < settings.PurgeBatchSize) break;
        }

        logger.LogInformation("PurgeWebhookDeliveriesJob: purged {Purged} deliveries older than {Cutoff}", purged, cutoff);
        return purged;
    }
}
