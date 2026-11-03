namespace Notification.Options;

/// <summary>Bound from <c>Notification</c>.</summary>
public sealed class NotificationOptions
{
    public NotificationChannel[] DefaultChannelOrder { get; set; } =
        [NotificationChannel.WhatsApp, NotificationChannel.Email];

    /// <summary>Days to keep rows in <c>notification.notification_log</c> (Notification:LogRetentionDays). Default: 90.</summary>
    public int LogRetentionDays { get; set; } = 90;

    /// <summary>Rows deleted per statement by the purge job (Notification:LogPurgeBatchSize). Default: 1000.</summary>
    public int LogPurgeBatchSize { get; set; } = 1000;

    /// <summary>
    /// Failed attempts on one channel (each Hangfire retry is one attempt) after which a transient,
    /// definitely-not-delivered failure (5xx / 429 / connection refused) falls back to the next channel
    /// (Notification:FallbackAfterAttempts). Ambiguous failures (timeouts) never fall back. Default: 3.
    /// </summary>
    public int FallbackAfterAttempts { get; set; } = 3;
}
