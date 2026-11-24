using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Search.Observability;

namespace Search.Domains.Analytics.Services;

/// <summary>
/// Every <c>Search:Analytics:FlushSeconds</c>, and once when the host stops, writes what this replica buffered:
/// one unit of work per tenant, in that tenant's scope (its database, RLS). It fails open: a tenant whose write fails
/// (or that is gone or migrating) loses those counts, logged; searches never wait for it.
/// </summary>
public sealed class SearchAnalyticsFlushService(
    SearchAnalyticsBuffer buffer,
    ITenantStore tenants,
    IUnitOfWorkRunner unitOfWork,
    IOptions<SearchOptions> options,
    TimeProvider time,
    SearchMetrics metrics,
    ILogger<SearchAnalyticsFlushService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Analytics.Enabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.Analytics.FlushSeconds), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await FlushAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping: StopAsync flushes what is left.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    /// <summary>Writes and clears the buffer; returns the tenants written.</summary>
    public async Task<int> FlushAsync(CancellationToken cancellationToken)
    {
        var batch = buffer.Drain();
        if (batch.DroppedTerms > 0)
        {
            metrics.AnalyticsTermsDropped(batch.DroppedTerms);
            logger.LogWarning("Search analytics: {Count} zero-result term(s) dropped (Search:Analytics:MaxPendingTerms reached)",
                batch.DroppedTerms);
        }

        if (batch.IsEmpty)
            return 0;

        var now = time.GetUtcNow().UtcDateTime;
        var written = 0;
        foreach (var tenantId in batch.Daily.Select(d => d.TenantId).Concat(batch.ZeroResults.Select(t => t.TenantId)).Distinct())
        {
            try
            {
                var tenant = await tenants.FindByIdAsync(tenantId, cancellationToken);
                if (tenant is null || tenant.Status == TenantStatus.Migrating)
                    continue;

                using (TenantScope.Begin(tenant))
                {
                    await unitOfWork.RunAsync((services, ct) => services.GetRequiredService<SearchAnalyticsStore>()
                        .WriteAsync(batch.Daily, batch.ZeroResults, now, ct), cancellationToken);
                }

                written++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Search analytics of tenant {TenantId} could not be written; these counts are dropped", tenantId);
            }
        }

        return written;
    }
}
