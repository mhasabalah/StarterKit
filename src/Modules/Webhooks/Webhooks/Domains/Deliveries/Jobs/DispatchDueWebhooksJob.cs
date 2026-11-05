using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.BackgroundJobs;
using Shared.Core.MultiTenancy.Data;

namespace Webhooks.Domains.Deliveries.Jobs;

/// <summary>
/// Safety net (recurring, every minute): re-enqueues pending deliveries whose job was lost, e.g. the process died between
/// committing the delivery rows and enqueuing their jobs, or a scheduled retry vanished. A delivery counts as lost when it
/// is overdue by more than <see cref="WebhooksOptions.SweepStuckAfterMinutes"/>. Runs once per tenant.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class DispatchDueWebhooksJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<WebhooksOptions> options,
    ILogger<DispatchDueWebhooksJob> logger)
{
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await DispatchCurrentTenantAsync(cancellationToken),
            nameof(DispatchDueWebhooksJob));
        return total;
    }

    /// <summary>Re-enqueues the CURRENT ambient tenant's overdue deliveries; returns how many.</summary>
    public async Task<int> DispatchCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        var settings = options.Value;
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(-settings.SweepStuckAfterMinutes);

        var ids = await unitOfWork.RunAsync((services, ct) =>
            services.GetRequiredService<WebhooksDbContext>().Deliveries
                .AsNoTracking()
                .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt != null && d.NextAttemptAt <= cutoff)
                .OrderBy(d => d.NextAttemptAt)
                .Select(d => d.Id)
                .Take(settings.SweepBatchSize)
                .ToListAsync(ct), CancellationToken.None);

        var enqueued = 0;
        foreach (var id in ids)
        {
            cancellationToken?.ThrowIfCancellationRequested();
            try
            {
                // Push NextAttemptAt to "now" first (optimistic): a second sweep run will not pick it up again.
                var claimed = await unitOfWork.RunAsync(async (services, ct) =>
                {
                    var db = services.GetRequiredService<WebhooksDbContext>();
                    var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.Id == id, ct);
                    if (delivery is null || delivery.Status != WebhookDeliveryStatus.Pending) return false;
                    delivery.MarkEnqueued(services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
                    await db.SaveChangesAsync(ct);
                    return true;
                }, CancellationToken.None);

                if (!claimed) continue;
                jobs.Enqueue<DeliverWebhookJob>(job => job.ExecuteAsync(id, CancellationToken.None));
                enqueued++;
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict())
            {
                logger.LogDebug(ex, "Webhook delivery {DeliveryId} was picked up concurrently; skipping", id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Re-enqueuing webhook delivery {DeliveryId} failed", id);
            }
        }

        if (enqueued > 0)
            logger.LogWarning("DispatchDueWebhooksJob: re-enqueued {Count} overdue webhook deliveries", enqueued);
        return enqueued;
    }
}
