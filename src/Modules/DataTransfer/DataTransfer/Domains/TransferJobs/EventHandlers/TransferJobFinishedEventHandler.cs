using DataTransfer.Domains.TransferJobs.Events;
using Notification.Contracts;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;

namespace DataTransfer.Domains.TransferJobs.EventHandlers;

/// <summary>
/// Tells the requester that their job finished: an in-app inbox entry (pushed in real time over the notification
/// hub), dispatched after the finishing unit of work committed. Best effort: a notification problem never fails the
/// job bookkeeping; the client can always poll <c>GET /data-transfer/jobs/{id}</c>.
/// </summary>
public sealed class TransferJobFinishedEventHandler(
    INotificationSender notifications,
    DataTransferDbContext dbContext,
    TransferProgressPublisher progress,
    ILogger<TransferJobFinishedEventHandler> logger) : INotificationHandler<TransferJobFinishedEvent>
{
    public const string Category = "data-transfer.job-finished";

    public async Task Handle(TransferJobFinishedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Live: the final counters to the open sessions (the progress stream ends here).
            var job = await dbContext.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == notification.JobId, cancellationToken);
            if (job is not null)
                await progress.PublishAsync(job, cancellationToken);

            var culture = TransferCulture.IsArabic(notification.Culture) ? NotificationCulture.Ar : NotificationCulture.En;
            await notifications
                .For(notification.RequestedBy, culture)
                .WithInApp(Content(notification))
                .WithPush()
                .SendAsync(ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not notify user {UserId} that transfer job {JobId} finished",
                notification.RequestedBy, notification.JobId);
        }
    }

    public static InAppContent Content(TransferJobFinishedEvent e)
    {
        var (en, ar, severity) = Describe(e.Kind, e.Status);
        return new InAppContent(
            Title: new LocalizedText(en, ar),
            Body: new LocalizedText($"{en}: {e.DefinitionKey}.", $"{ar}: {e.DefinitionKey}."),
            Category: Category,
            ActionUrl: $"/data-transfer/jobs/{e.JobId}",
            Severity: severity,
            Data: new Dictionary<string, string>
            {
                ["jobId"] = e.JobId.ToString(),
                ["kind"] = e.Kind.ToString(),
                ["status"] = e.Status.ToString()
            });
    }

    private static (string En, string Ar, NotificationSeverity Severity) Describe(TransferJobKind kind, TransferJobStatus status)
    {
        var import = kind == TransferJobKind.Import;
        return status switch
        {
            TransferJobStatus.Completed => (import ? "Import completed" : "Export ready",
                import ? "اكتمل الاستيراد" : "التصدير جاهز", NotificationSeverity.Success),
            TransferJobStatus.CompletedWithErrors => (import ? "Import completed with errors" : "Export completed with errors",
                import ? "اكتمل الاستيراد مع أخطاء" : "اكتمل التصدير مع أخطاء", NotificationSeverity.Warning),
            TransferJobStatus.Cancelled => (import ? "Import cancelled" : "Export cancelled",
                import ? "تم إلغاء الاستيراد" : "تم إلغاء التصدير", NotificationSeverity.Info),
            _ => (import ? "Import failed" : "Export failed",
                import ? "فشل الاستيراد" : "فشل التصدير", NotificationSeverity.Critical)
        };
    }
}
