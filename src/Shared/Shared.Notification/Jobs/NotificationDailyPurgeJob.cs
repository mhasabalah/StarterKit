namespace Shared.Notification.Jobs;

public sealed class NotificationDailyPurgeJob(
    NotificationLogRepository repository,
    ILogger<NotificationDailyPurgeJob> logger)
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow - RetentionPeriod;
        var deleted = await repository.PurgeOlderThanAsync(threshold, ct);

        logger.LogInformation(
            "Notification log purge complete. Deleted {Count} records older than {Threshold:O}.",
            deleted, threshold);

        // Verification codes are ephemeral (10-minute TTL set per key in Redis).
        // Redis handles their expiry automatically — no purge needed here.
    }
}
