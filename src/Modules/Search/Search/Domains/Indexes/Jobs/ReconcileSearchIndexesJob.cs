using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Search.Domains.Indexes.Jobs;

/// <summary>
/// Recurring (<c>Search:Reconcile:Cron</c>, and once at startup): brings every index of every tenant up to its
/// definition. It syncs the engine's index settings once, then, per tenant, enqueues a rebuild of each index that was
/// never built or was built from another definition (its fingerprint changed: a field, a weight, the normalizer).
/// A failed rebuild is retried at most once per <see cref="RetryFailedAfter"/>.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class ReconcileSearchIndexesJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    SearchIndexRegistry registry,
    IBackgroundJobClient jobs,
    TimeProvider time,
    ILogger<ReconcileSearchIndexesJob> logger)
{
    public static readonly TimeSpan RetryFailedAfter = TimeSpan.FromHours(1);

    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        if (registry.Indexes.Count == 0)
            return 0;

        foreach (var index in registry.Indexes)
            await unitOfWork.RunAsync((sp, ct) => sp.GetRequiredService<ISearchEngine>().EnsureIndexAsync(index, ct));

        var enqueued = 0;
        await tenantJobs.ForEachTenantScopeAsync(
            async (_, _) => enqueued += await ReconcileCurrentTenantAsync(cancellationToken),
            nameof(ReconcileSearchIndexesJob));

        if (enqueued > 0)
            logger.LogInformation("ReconcileSearchIndexesJob: {Count} rebuild(s) enqueued", enqueued);
        return enqueued;
    }

    /// <summary>Enqueues the rebuilds the CURRENT tenant needs; returns how many.</summary>
    public async Task<int> ReconcileCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        cancellationToken?.ThrowIfCancellationRequested();
        var now = time.GetUtcNow().UtcDateTime;
        var states = await unitOfWork.RunAsync((sp, ct) =>
            sp.GetRequiredService<SearchDbContext>().IndexStates.AsNoTracking().ToListAsync(ct));

        var enqueued = 0;
        foreach (var index in registry.Indexes)
        {
            var state = states.FirstOrDefault(s => s.IndexKey == index.Key);
            if (state is not null && !NeedsRebuild(state, index, now))
                continue;

            jobs.Enqueue<RebuildSearchIndexJob>(job => job.ExecuteAsync(index.Key, false, JobCancellationToken.Null));
            enqueued++;
        }

        return enqueued;
    }

    internal static bool NeedsRebuild(Models.SearchIndexState state, RegisteredIndex index, DateTime now) =>
        state.IsStale(index.Fingerprint)
        && !state.IsLeased(now)
        && !(state.LastErrorCode is not null && state.BuildStartedAt > now - RetryFailedAfter);
}
