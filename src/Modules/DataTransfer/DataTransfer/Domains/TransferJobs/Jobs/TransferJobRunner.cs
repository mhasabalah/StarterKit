using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Entitlements;

namespace DataTransfer.Domains.TransferJobs.Jobs;

/// <summary>Another worker took the job over (its lease expired): this run stops without touching the row.</summary>
public sealed class TransferLeaseLostException(Guid jobId)
    : InvalidOperationException($"Transfer job {jobId} is now owned by another worker.");

/// <summary>
/// Shared plumbing of <see cref="ProcessImportJob"/> and <see cref="ProcessExportJob"/>: claim (lease), checked updates
/// under the lease, and the retry model.
/// <para>
/// <b>Retry model</b> (single layer, Hangfire's own retry is off): a failed attempt releases the lease and schedules
/// the next one with backoff, up to <see cref="DataTransferOptions.MaxAttempts"/>; then the job is <c>Failed</c>
/// (<c>internal_error</c>). A file problem (<see cref="InvalidTransferFileException"/>) fails at once: retrying cannot
/// fix the file. A crashed worker leaves an expiring lease; <see cref="RequeueStalledTransferJobsJob"/> queues the job
/// again. Server shutdown releases the lease and lets Hangfire re-run the aborted job. A 4xx from another module
/// (<see cref="IsPermanent"/>) fails at once too.
/// </para>
/// <para>
/// <b>Requester.</b> Every attempt re-checks that the requester is still allowed (membership + permissions) and runs
/// in their current branch scope and language; a requester who lost access stops the job
/// (<c>requester_no_longer_authorized</c>).
/// </para>
/// </summary>
public abstract class TransferJobRunner(
    IUnitOfWorkRunner unitOfWork,
    ITenantContext tenantContext,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<DataTransferOptions> options,
    DataTransferMetrics metrics,
    TransferProgressPublisher progress,
    ILogger logger)
{
    private const int UpdateRetries = 3;

    protected IUnitOfWorkRunner UnitOfWork { get; } = unitOfWork;
    protected DataTransferOptions Settings => options.Value;
    protected DataTransferMetrics Metrics { get; } = metrics;
    protected ILogger Logger { get; } = logger;
    protected DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
    protected TimeSpan Lease => TimeSpan.FromSeconds(Settings.LeaseSeconds);

    /// <summary>The work of one attempt, after the claim.</summary>
    protected abstract Task RunAsync(TransferJob claimed, string owner, CancellationToken cancellationToken);

    /// <summary>Queues the next attempt (the concrete job type).</summary>
    protected abstract void ScheduleRetry(Guid jobId, TimeSpan delay);

    /// <summary>Entry point of the Hangfire job.</summary>
    protected async Task ExecuteCoreAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
        var claimed = await TryClaimAsync(jobId, owner);
        if (claimed is null)
            return;

        // Messages (row errors, failures, headers) in the requester's language for the whole run.
        using var culture = TransferText.UseCulture(claimed.Culture);
        try
        {
            var requester = await ResolveRequesterAsync(claimed);
            if (requester is null)
            {
                Logger.LogWarning("Transfer job {JobId}: requester {UserId} lost access; the job is stopped",
                    jobId, claimed.RequestedBy);
                await FailAsync(jobId, owner, "requester_no_longer_authorized",
                    TransferText.Get("Transfer.Job.requester_no_longer_authorized"));
                return;
            }

            // The definition reads and writes in the requester's CURRENT branch scope, exactly like their requests, and
            // as the requester (audit columns, the single-tenant plan subscriber).
            using var scope = requester.Membership is { } membership && tenantContext.Tenant is { } tenant
                ? TenantScope.Begin(tenant, membership: membership)
                : null;
            using var actingUser = ActingUser.Begin(claimed.RequestedBy);
            await progress.PublishAsync(claimed, CancellationToken.None);
            await RunAsync(claimed, owner, cancellationToken);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(claimed, owner, ex, cancellationToken);
        }
    }

    private async Task HandleFailureAsync(TransferJob claimed, string owner, Exception ex, CancellationToken cancellationToken)
    {
        switch (ex)
        {
            case TransferLeaseLostException:
                Logger.LogWarning("Transfer job {JobId}: the lease was taken over by another worker; this run stops", claimed.Id);
                return;
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                Logger.LogInformation("Transfer job {JobId} interrupted by shutdown; the lease is released", claimed.Id);
                await UpdateUnderLeaseAsync(claimed.Id, owner, job => job.ReleaseLease());
                throw new OperationCanceledException("The server is shutting down.", ex, cancellationToken);
            case InvalidTransferFileException file:
                Logger.LogInformation("Transfer job {JobId} failed: {Code} {Message}", claimed.Id, file.ErrorCode, file.Message);
                await FailAsync(claimed.Id, owner, file.ErrorCode ?? "invalid_file", file.Message);
                return;
            case QuotaExceededException quota:
                Logger.LogInformation("Transfer job {JobId} stopped: plan quota {Key} used up ({Used}/{Limit})",
                    claimed.Id, quota.Key, quota.Used, quota.Limit);
                await FailAsync(claimed.Id, owner, QuotaExceededException.ErrorCodeValue,
                    TransferText.Get("Transfer.Job.quota_exceeded", quota.Used, quota.Limit));
                return;
            case CustomException rejected when IsPermanent(rejected):
                // A 4xx from a module (Attachment refused the file, the definition or source is gone): retrying cannot
                // change the answer.
                Logger.LogWarning("Transfer job {JobId} rejected: {Status} {Code}", claimed.Id, rejected.StatusCode,
                    rejected.ErrorCode);
                await FailAsync(claimed.Id, owner, rejected.ErrorCode ?? "rejected",
                    TransferText.Get("Transfer.Job.rejected", rejected.Message));
                return;
            default:
                Logger.LogError(ex, "Transfer job {JobId} attempt {Attempt} failed", claimed.Id, claimed.Attempts);
                await RetryOrFailAsync(claimed.Id, owner);
                return;
        }
    }

    /// <summary>A 4xx other than conflict, timeout and throttling (those may succeed on the next attempt).</summary>
    internal static bool IsPermanent(CustomException ex) =>
        (int)ex.StatusCode is >= 400 and < 500 &&
        ex.StatusCode is not (System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.RequestTimeout);

    private sealed record Requester(TenantMembershipInfo? Membership);

    /// <summary>
    /// Re-checks at every attempt that the requester may still do this. Multi-tenant mode: they must still be an active
    /// member holding the DataTransfer permission AND the definition's permission; their membership also gives the
    /// branch scope. Single-tenant mode has no memberships: the platform permissions checked at creation stand.
    /// Null = no longer authorized.
    /// </summary>
    private async Task<Requester?> ResolveRequesterAsync(TransferJob job)
    {
        if (!tenantContext.MultiTenancyEnabled || tenantContext.TenantId is not { } tenantId)
            return new Requester(null);

        return await UnitOfWork.RunAsync(async (services, ct) =>
        {
            var registry = services.GetRequiredService<TransferDefinitionRegistry>();
            ITransferDefinition definition = job.Kind == TransferJobKind.Import
                ? registry.GetImport(job.DefinitionKey)
                : registry.GetExport(job.DefinitionKey);
            var membership = await services.GetRequiredService<ITenantMembershipResolver>()
                .GetMembershipAsync(job.RequestedBy, tenantId, ct);
            var transferPermission = job.Kind == TransferJobKind.Import
                ? TenantPermissions.DataImport
                : TenantPermissions.DataExport;

            return membership is not null &&
                   membership.HasPermission(transferPermission) &&
                   membership.HasPermission(definition.RequiredPermission)
                ? new Requester(membership)
                : null;
        }, CancellationToken.None);
    }

    /// <summary>Claims the job; null when there is nothing to do (finished, gone, or leased by another worker).</summary>
    private async Task<TransferJob?> TryClaimAsync(Guid jobId, string owner)
    {
        try
        {
            return await UnitOfWork.RunAsync(async (services, ct) =>
            {
                var repository = services.GetRequiredService<ITransferJobRepository>();
                var job = await repository.GetByIdAsync(jobId, ct);
                if (job is null || !job.TryClaim(owner, Now, Lease))
                    return null;

                await repository.SaveChangesAsync(ct);
                return job;
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            Logger.LogInformation("Transfer job {JobId} was claimed by another worker", jobId);
            return null;
        }
    }

    /// <summary>
    /// Loads the job, checks this run still holds the lease, applies <paramref name="change"/>, renews the lease and
    /// saves. A concurrent change of the row (a cancel request) is reloaded and applied again.
    /// </summary>
    protected async Task<T> UpdateUnderLeaseAsync<T>(Guid jobId, string owner,
        Func<TransferJob, ITransferJobRepository, T> change)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await UnitOfWork.RunAsync(async (services, ct) =>
                {
                    var repository = services.GetRequiredService<ITransferJobRepository>();
                    var job = await repository.GetByIdAsync(jobId, ct);
                    if (job is null || !job.HoldsLease(owner))
                        throw new TransferLeaseLostException(jobId);

                    job.RenewLease(Now, Lease);
                    var result = change(job, repository);
                    await repository.SaveChangesAsync(ct);
                    if (job.IsTerminal)
                        Metrics.RecordFinished(job); // the final state is pushed by TransferJobFinishedEventHandler
                    else
                        await progress.PublishAsync(job, CancellationToken.None);
                    return result;
                }, CancellationToken.None);
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict() && attempt < UpdateRetries)
            {
                Logger.LogDebug(ex, "Transfer job {JobId} changed concurrently; applying again", jobId);
            }
        }
    }

    protected Task UpdateUnderLeaseAsync(Guid jobId, string owner, Action<TransferJob> change) =>
        UpdateUnderLeaseAsync(jobId, owner, (job, _) =>
        {
            change(job);
            return true;
        });

    /// <summary>
    /// Between two batches: true to go on; false when the user asked to cancel (the job is then marked
    /// <c>Cancelled</c> in the same save).
    /// </summary>
    protected bool ContinueOrCancel(TransferJob job)
    {
        if (!job.CancelRequested)
            return true;
        job.ConfirmCancelled(Now, Settings.RetentionDays);
        return false;
    }

    protected Task FailAsync(Guid jobId, string owner, string code, string message) =>
        UpdateUnderLeaseAsync(jobId, owner, job => job.Fail(code, message, Now, Settings.RetentionDays));

    private async Task RetryOrFailAsync(Guid jobId, string owner)
    {
        try
        {
            var attempts = await UpdateUnderLeaseAsync(jobId, owner, (job, _) =>
            {
                if (job.Attempts >= Settings.MaxAttempts)
                {
                    job.Fail("internal_error", TransferText.Get("Transfer.Job.internal_error", job.Attempts), Now,
                        Settings.RetentionDays);
                    return 0;
                }

                job.ReleaseLease();
                job.MarkEnqueued(Now);
                return job.Attempts;
            });

            if (attempts > 0)
                ScheduleRetry(jobId, RetryDelay(attempts));
        }
        catch (TransferLeaseLostException)
        {
            Logger.LogWarning("Transfer job {JobId}: lease lost while recording a failure", jobId);
        }
    }

    /// <summary>
    /// Counts <paramref name="rows"/> against the plan quota <see cref="EntitlementKeys.DataTransferRows"/> (tenant in
    /// multi-tenant mode, the acting requester otherwise). Throws <see cref="QuotaExceededException"/> when the plan does
    /// not allow it: the job then stops with <c>quota_exceeded</c>, and what was committed before stays committed.
    /// Fails open when the counter store is down (<see cref="IQuotaService"/> contract).
    /// </summary>
    protected static async Task ConsumeRowsAsync(IServiceProvider services, long rows, CancellationToken cancellationToken)
    {
        if (rows <= 0)
            return;
        var result = await services.GetRequiredService<IQuotaService>()
            .TryConsumeAsync(EntitlementKeys.DataTransferRows, rows, cancellationToken);
        if (!result.Allowed)
            throw new QuotaExceededException(EntitlementKeys.DataTransferRows, result.Used, result.Limit);
    }

    /// <summary>30 s, 2 min, 8 min, ... (capped at 1 h).</summary>
    public static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(4, Math.Max(0, attempts - 1)), 3600));

    protected IBackgroundJobClient Jobs => jobs;
}
