namespace Notification.Dispatch;

/// <summary>
/// Daily retention job of the Notification module:
/// <list type="bullet">
/// <item><c>notification_log</c> rows older than Notification:LogRetentionDays;</item>
/// <item>inbox entries past their expiry (Inbox:RetentionDays after creation);</item>
/// <item>push devices not re-registered for Push:StaleDeviceDays (uninstalled apps, abandoned devices).</item>
/// </list>
/// Batch size: Notification:LogPurgeBatchSize. One-time codes are not stored here (Identity owns them).
/// Each part runs independently: one failing does not stop the others.
/// </summary>
public sealed class NotificationDailyPurgeJob(
    INotificationLogRepository repository,
    IInboxNotificationRepository inbox,
    IPushDeviceRepository devices,
    IOptions<NotificationOptions> options,
    IOptions<PushOptions> pushOptions,
    TimeProvider timeProvider,
    ILogger<NotificationDailyPurgeJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var failures = new List<Exception>();

        if (opts.LogRetentionDays <= 0)
        {
            logger.LogWarning(
                "Notification log purge skipped: Notification:LogRetentionDays is {Days} (must be > 0).",
                opts.LogRetentionDays);
        }
        else
        {
            await RunAsync("notification log", failures, async () =>
            {
                var threshold = now.AddDays(-opts.LogRetentionDays);
                var deleted = await repository.PurgeOlderThanAsync(threshold, opts.LogPurgeBatchSize, ct);
                logger.LogInformation(
                    "Notification log purge complete. Deleted {Count} records older than {Threshold:O}.", deleted, threshold);
            });
        }

        await RunAsync("inbox", failures, async () =>
        {
            var deleted = await inbox.PurgeExpiredAsync(now, opts.LogPurgeBatchSize, ct);
            logger.LogInformation("Inbox purge complete. Deleted {Count} expired entries.", deleted);
        });

        await RunAsync("push devices", failures, async () =>
        {
            var threshold = now.AddDays(-pushOptions.Value.StaleDeviceDays);
            var deleted = await devices.PurgeStaleAsync(threshold, opts.LogPurgeBatchSize, ct);
            logger.LogInformation("Push device purge complete. Deleted {Count} devices not seen since {Threshold:O}.", deleted, threshold);
        });

        if (failures.Count > 0)
            throw new AggregateException("Notification retention purge failed partially.", failures);
    }

    private async Task RunAsync(string part, List<Exception> failures, Func<Task> purge)
    {
        try
        {
            await purge();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Notification retention purge of {Part} failed", part);
            failures.Add(ex);
        }
    }
}
