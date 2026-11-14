using Agentic.Contracts.Approvals;
using Agentic.Telemetry;
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
/// <para>
/// Each full pass also refreshes the backlog gauges of <see cref="AgenticMetrics"/> (pending approvals, the oldest one,
/// the oldest queued run) that the Prometheus alerts read.
/// </para>
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class RequeueStalledAgentRunsJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    AgenticMetrics metrics,
    ILogger<RequeueStalledAgentRunsJob> logger)
{
    internal const int BatchSize = 100;

    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        if (!options.Value.Enabled)
            return 0;

        var total = 0;
        var backlog = new Backlog(0, null, null);
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) =>
        {
            total += await RequeueCurrentTenantAsync(cancellationToken);
            backlog = backlog.Add(await MeasureCurrentTenantAsync());
        }, nameof(RequeueStalledAgentRunsJob));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        metrics.RecordBacklog(backlog.PendingApprovals, now - (backlog.OldestApproval ?? now), now - (backlog.OldestQueued ?? now));
        return total;
    }

    /// <summary>Pending approvals and the oldest waiting approval and queued run of the current tenant.</summary>
    public Task<Backlog> MeasureCurrentTenantAsync() =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<AgenticDbContext>();
            var pending = db.Approvals.Where(a => a.Status == AgentApprovalStatus.Pending);
            var count = await pending.LongCountAsync(ct);
            var oldestApproval = count == 0 ? null : await pending.MinAsync(a => (DateTime?)a.RequestedAt, ct);
            // ReadyAt, not LastEnqueuedAt: the sweep re-enqueues stalled runs and would reset the age it measures. A retry
            // still in its back-off is not waiting for a worker yet.
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var oldestQueued = await db.Runs
                .Where(r => r.Status == AgentRunStatus.Queued && (r.ReadyAt ?? r.CreatedAt) <= now)
                .MinAsync(r => r.ReadyAt ?? r.CreatedAt, ct);
            return new Backlog(count, oldestApproval, oldestQueued);
        }, CancellationToken.None);

    public sealed record Backlog(long PendingApprovals, DateTime? OldestApproval, DateTime? OldestQueued)
    {
        public Backlog Add(Backlog other) => new(PendingApprovals + other.PendingApprovals,
            Earliest(OldestApproval, other.OldestApproval), Earliest(OldestQueued, other.OldestQueued));

        private static DateTime? Earliest(DateTime? a, DateTime? b) => a is null ? b : b is null ? a : a < b ? a : b;
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

        metrics.RecordRequeued(stalled.Count);

        return stalled.Count;
    }
}
