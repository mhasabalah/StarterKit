namespace Shared.Notification.Persistence;

public sealed class NotificationLog
{
    public Guid Id { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid UserId { get; set; }
    public NotificationChannel Channel { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Queued;
    public string? Error { get; set; }
    public string? MessageJson { get; set; }
    public string? HangfireJobId { get; set; }
    public NotificationCulture Culture { get; set; } = NotificationCulture.En;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}
