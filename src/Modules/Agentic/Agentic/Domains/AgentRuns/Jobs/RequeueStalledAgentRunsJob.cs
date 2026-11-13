using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Agentic.Domains.AgentRuns.Jobs;

/// <summary>
/// Safety net (recurring, every few minutes, per tenant): queues again every waiting or running run without a live
/// lease that was not queued recently. That covers the gaps a commit-then-enqueue design leaves open: a crash between
/// saving the run and enqueueing its job, a retry that was recorded but never scheduled, a worker that died holding the
/// lease, and a failure that could not be recorded. Duplicate jobs are harmless (the lease lets only one proceed).
/// Runs suspended for an approval are not touched: they wait for a person, not for a worker.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class RequeueStalledAgentRunsJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    ILogger<RequeueStalledAgentRunsJob> logger)
{
    internal const int BatchSize = 100;

    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        if (!options.Value.Enabled)
            return 0;

        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await RequeueCurrentTenantAsync(cancellationToken),
            nameof(RequeueStalledAgentRunsJob));
        return total;
    }

    public async Task<int> RequeueCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        cancellationToken?.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var quiet = TimeSpan.FromMinutes(options.Value.StalledAfterMinutes);
        var cutoff = now - quiet;

        var stalled = await unitOfWork.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<AgenticDbContext>();
            var candidates = await db.Runs
                .Where(r => r.Status == AgentRunStatus.Queued || r.Status == AgentRunStatus.Running)
                .Where(r => r.LeaseUntil == null || r.LeaseUntil < now)
                .Where(r => r.LastEnqueuedAt == null || r.LastEnqueuedAt < cutoff)
                .OrderBy(r => r.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);

            var due = candidates.Where(r => r.IsStalled(now, quiet)).ToList();
            foreach (var run in due)
                run.MarkEnqueued(now);
            await db.SaveChangesAsync(ct);
            return due.Select(r => r.Id).ToList();
        }, CancellationToken.None);

        foreach (var id in stalled)
        {
            AgentRunQueue.Enqueue(jobs, id);
            logger.LogWarning("Agent run {RunId} was stalled and has been queued again", id);
        }

        return stalled.Count;
    }
}
