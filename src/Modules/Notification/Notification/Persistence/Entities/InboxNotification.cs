namespace Notification.Persistence.Entities;

/// <summary>
/// One entry of a user's in-app inbox (<c>notification.inbox_notification</c>). Written by <c>InAppDeliveryJob</c>
/// exactly once per notification: unique (<see cref="UserId"/>, <see cref="CorrelationId"/>) makes Hangfire retries
/// idempotent. Text is stored already rendered in the recipient's language.
/// </summary>
public sealed class InboxNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Tenant the notification belongs to (null = platform notice, visible in every tenant). Purged with the tenant.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>The <c>notification_log</c> correlation id of the send that produced this entry.</summary>
    public Guid CorrelationId { get; set; }

    public string Category { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public Dictionary<string, string>? Data { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    /// <summary>Purged after this time (<c>Inbox:RetentionDays</c> after creation).</summary>
    public DateTime ExpiresAt { get; set; }
}
