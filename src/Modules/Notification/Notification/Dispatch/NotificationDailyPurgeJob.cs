namespace Notification.Dispatch;

/// <summary>
/// Daily retention job for <c>notification.notification_log</c>.
/// Retention and batch size come from Notification:LogRetentionDays / Notification:LogPurgeBatchSize.
/// One-time codes are not stored here (Identity owns them in identity.otp_codes).
/// </summary>
public sealed class NotificationDailyPurgeJob(
    INotificationLogRepository repository,
    IOptions<NotificationOptions> options,
    ILogger<NotificationDailyPurgeJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        if (opts.LogRetentionDays <= 0)
        {
            logger.LogWarning(
                "Notification log purge skipped: Notification:LogRetentionDays is {Days} (must be > 0).",
                opts.LogRetentionDays);
            return;
        }

        var threshold = DateTime.UtcNow.AddDays(-opts.LogRetentionDays);
        var deleted = await repository.PurgeOlderThanAsync(threshold, opts.LogPurgeBatchSize, ct);

        logger.LogInformation(
            "Notification log purge complete. Deleted {Count} records older than {Threshold:O}.",
            deleted, threshold);
    }
}
