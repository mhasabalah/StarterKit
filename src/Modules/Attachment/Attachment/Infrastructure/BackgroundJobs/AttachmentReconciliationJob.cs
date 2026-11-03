using Attachment.Infrastructure.Maintenance;
using Hangfire;

namespace Attachment.Infrastructure.BackgroundJobs;

/// <summary>
/// Recurring DB/blob reconciliation (<c>Attachment:Reconciliation:Cron</c>). The work lives in
/// <see cref="AttachmentReconciler"/>, which does every DB change through <see cref="IUnitOfWorkRunner"/> (one unit
/// of work per row), so the events of committed changes are published and one bad row never aborts the run.
/// No automatic retry: the next scheduled run is the retry.
/// </summary>
public class AttachmentReconciliationJob(
    AttachmentReconciler reconciler,
    ILogger<AttachmentReconciliationJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var report = await reconciler.RunAsync(cancellationToken);

        logger.LogInformation(
            "Attachment reconciliation: {Abandoned} abandoned upload(s), {Deleted} deletion(s) completed, " +
            "{Failed} failed, {Orphans} orphan blob(s) ({OrphansDeleted} deleted), {Purged} pending row(s) purged",
            report.AbandonedUploadsClaimed, report.DeletionsCompleted, report.DeletionsFailed,
            report.OrphansFound, report.OrphansDeleted, report.PendingRowsPurged);
    }
}
