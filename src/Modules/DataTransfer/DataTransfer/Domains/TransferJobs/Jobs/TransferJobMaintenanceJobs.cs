using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace DataTransfer.Domains.TransferJobs.Jobs;

/// <summary>
/// Safety net (recurring, every few minutes, per tenant): queues again every unfinished job without a live lease
/// that was not queued recently: a lost enqueue after creation, a crashed worker, an expired lease. Duplicate runs are
/// harmless (the lease lets only one proceed).
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class RequeueStalledTransferJobsJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<DataTransferOptions> options,
    ILogger<RequeueStalledTransferJobsJob> logger)
{
    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await RequeueCurrentTenantAsync(cancellationToken),
            nameof(RequeueStalledTransferJobsJob));
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
            var db = services.GetRequiredService<DataTransferDbContext>();
            var candidates = await db.Jobs
                .Where(j => j.Status == TransferJobStatus.Pending || j.Status == TransferJobStatus.Running)
                .Where(j => j.LeaseUntil == null || j.LeaseUntil < now)
                .Where(j => j.LastEnqueuedAt == null || j.LastEnqueuedAt < cutoff)
                .OrderBy(j => j.CreatedAt)
                .Take(100)
                .ToListAsync(ct);

            var due = candidates.Where(j => j.IsStalled(now, quiet)).ToList();
            foreach (var job in due)
                job.MarkEnqueued(now);
            await db.SaveChangesAsync(ct);
            return due.Select(j => (j.Id, j.Kind)).ToList();
        }, CancellationToken.None);

        foreach (var (id, kind) in stalled)
        {
            TransferJobQueue.Enqueue(jobs, id, kind);
            logger.LogWarning("Transfer job {JobId} ({Kind}) was stalled and has been queued again", id, kind);
        }

        return stalled.Count;
    }
}

/// <summary>
/// Retention (recurring, nightly, per tenant): deletes expired jobs, their files (through Attachment) and their
/// stored errors. A dry run is kept while a commit job that reuses its source file is still alive.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class PurgeExpiredTransferJobsJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    TimeProvider timeProvider,
    ILogger<PurgeExpiredTransferJobsJob> logger)
{
    internal const int BatchSize = 100;
    internal const int MaxBatchesPerRun = 50;

    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await PurgeCurrentTenantAsync(cancellationToken),
            nameof(PurgeExpiredTransferJobsJob));
        return total;
    }

    /// <summary>
    /// Purges the CURRENT tenant's expired jobs in batches until none is left (or <see cref="MaxBatchesPerRun"/> is
    /// reached; the next run continues). Keyset paging on (expires_at, id): a job that fails to purge is skipped, never
    /// retried in a loop.
    /// </summary>
    public async Task<int> PurgeCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var purged = 0;
        (DateTime ExpiresAt, Guid Id)? after = null;

        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            var due = await NextBatchAsync(now, after);
            foreach (var (id, _) in due)
            {
                cancellationToken?.ThrowIfCancellationRequested();
                if (await TryPurgeOneAsync(id))
                    purged++;
            }

            if (due.Count < BatchSize)
                break;
            after = (due[^1].ExpiresAt, due[^1].Id);
        }

        logger.LogInformation("PurgeExpiredTransferJobsJob: purged {Purged} job(s)", purged);
        return purged;
    }

    private Task<List<(Guid Id, DateTime ExpiresAt)>> NextBatchAsync(DateTime now, (DateTime ExpiresAt, Guid Id)? after) =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<DataTransferDbContext>();
            var expired = db.Jobs.AsNoTracking()
                .Where(j => j.ExpiresAt != null && j.ExpiresAt < now)
                .Where(j => !db.Jobs.Any(c => c.DryRunJobId == j.Id && (c.ExpiresAt == null || c.ExpiresAt >= now)));
            if (after is { } cursor)
            {
                expired = expired.Where(j => j.ExpiresAt > cursor.ExpiresAt ||
                                             (j.ExpiresAt == cursor.ExpiresAt && j.Id.CompareTo(cursor.Id) > 0));
            }

            var rows = await expired
                .OrderBy(j => j.ExpiresAt).ThenBy(j => j.Id)
                .Select(j => new { j.Id, ExpiresAt = j.ExpiresAt!.Value })
                .Take(BatchSize)
                .ToListAsync(ct);
            return rows.Select(r => (r.Id, r.ExpiresAt)).ToList();
        }, CancellationToken.None);

    private async Task<bool> TryPurgeOneAsync(Guid jobId)
    {
        try
        {
            await PurgeOneAsync(jobId);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Transfer job {JobId} could not be purged; the next run tries again", jobId);
            return false;
        }
    }

    /// <summary>Files first (hidden at once, removed by Attachment's deletion job), then the row (errors cascade).</summary>
    private Task PurgeOneAsync(Guid jobId) =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            await services.GetRequiredService<ISender>()
                .Send(new DeleteAttachmentsByEntityCommand(EntityType.DataTransferJob, jobId), ct);
            await services.GetRequiredService<DataTransferDbContext>().Jobs
                .Where(j => j.Id == jobId).ExecuteDeleteAsync(ct);
        }, CancellationToken.None);
}

/// <summary>Queues the Hangfire job for a transfer job row (the <c>data-transfer</c> queue comes from the job attributes).</summary>
public static class TransferJobQueue
{
    public static string Enqueue(IBackgroundJobClient jobs, Guid jobId, TransferJobKind kind) =>
        kind == TransferJobKind.Import
            ? jobs.Enqueue<ProcessImportJob>(job => job.ExecuteAsync(jobId, CancellationToken.None))
            : jobs.Enqueue<ProcessExportJob>(job => job.ExecuteAsync(jobId, CancellationToken.None));
}
