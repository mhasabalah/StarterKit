using Attachment.Infrastructure.Configuration;
using Hangfire;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.BackgroundJobs;

/// <summary>
/// Schedules the recurring Attachment jobs: the pending-attachment cleanup
/// (<c>Attachment:Pending:CleanupIntervalHours</c>) and the DB/blob reconciliation sweeper
/// (<c>Attachment:Reconciliation:Cron</c>; removed when <c>Attachment:Reconciliation:Enabled</c> is false).
/// </summary>
public class JobSchedulerBootstrap(
    IOptions<AttachmentOptions> options,
    ILogger<JobSchedulerBootstrap> logger) : IHostedService
{
    public const string CleanupJobId = "cleanup-expired-attachments";
    public const string ReconciliationJobId = "attachment-reconciliation";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cron = CleanupCron(options.Value.Pending.CleanupIntervalHours);
        var utc = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };

        RecurringJob.AddOrUpdate<CleanupExpiredPendingAttachmentsJob>(CleanupJobId, job => job.ExecuteAsync(), cron, utc);
        logger.LogInformation("Scheduled {JobId} with cron {Cron}", CleanupJobId, cron);

        var reconciliation = options.Value.Reconciliation;
        if (reconciliation.Enabled)
        {
            RecurringJob.AddOrUpdate<AttachmentReconciliationJob>(
                ReconciliationJobId, job => job.ExecuteAsync(CancellationToken.None), reconciliation.Cron, utc);
            logger.LogInformation("Scheduled {JobId} with cron {Cron}", ReconciliationJobId, reconciliation.Cron);
        }
        else
        {
            RecurringJob.RemoveIfExists(ReconciliationJobId);
            logger.LogWarning("{JobId} is disabled: abandoned uploads and failed blob deletions are not retried",
                ReconciliationJobId);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static string CleanupCron(int intervalHours) =>
        intervalHours >= 24 ? Cron.Daily() : Cron.HourInterval(Math.Max(1, intervalHours));
}
