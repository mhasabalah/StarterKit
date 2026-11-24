using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Search.Domains.Analytics.Jobs;

/// <summary>
/// Recurring (<c>Search:Analytics:PurgeCron</c>): removes, in every tenant, the daily counts and zero-result terms older
/// than <c>Search:Analytics:RetentionDays</c>. Set-based deletes, one unit of work per tenant.
/// </summary>
public sealed class PurgeSearchAnalyticsJob(
    ITenantJobRunner tenantJobs,
    IOptions<SearchOptions> options,
    TimeProvider time,
    ILogger<PurgeSearchAnalyticsJob> logger)
{
    [DisableConcurrentExecution(60)]
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var cutoff = Cutoff();
        var purged = 0;
        await tenantJobs.ForEachTenantAsync(async (services, ct) =>
        {
            cancellationToken?.ThrowIfCancellationRequested();
            purged += await PurgeAsync(services.GetRequiredService<SearchDbContext>(), cutoff, ct);
        }, nameof(PurgeSearchAnalyticsJob));

        if (purged > 0)
            logger.LogInformation("PurgeSearchAnalyticsJob: {Count} analytics row(s) older than {Cutoff} removed", purged, cutoff);
        return purged;
    }

    /// <summary>The first day kept.</summary>
    public DateOnly Cutoff() =>
        DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-options.Value.Analytics.RetentionDays);

    private static async Task<int> PurgeAsync(SearchDbContext db, DateOnly cutoff, CancellationToken cancellationToken) =>
        await db.DailyStats.Where(s => s.Day < cutoff).ExecuteDeleteAsync(cancellationToken)
        + await db.ZeroResultTerms.Where(t => t.Day < cutoff).ExecuteDeleteAsync(cancellationToken);
}
