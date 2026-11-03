namespace Notification.Dispatch;

/// <summary>
/// Daily operator signal (recurring job <c>notification-daily-failure-summary</c>): how many notifications of the last
/// 24 hours failed on every channel and were never delivered. Logs a warning (alertable in Seq) when there are any;
/// details: <c>GET /notifications/history?status=Failed</c>.
/// </summary>
public sealed class NotificationDailyFailureSummaryJob(
    INotificationLogRepository repository,
    TimeProvider timeProvider,
    ILogger<NotificationDailyFailureSummaryJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-24);
        var undelivered = await repository.CountUndeliveredSinceAsync(since, ct);

        if (undelivered == 0)
            logger.LogInformation("Daily notification summary: no undelivered notifications in the last 24 hours.");
        else
            logger.LogWarning(
                "Daily notification summary: {UndeliveredCount} notification(s) of the last 24 hours were not delivered on any channel.",
                undelivered);
    }
}
