namespace Shared.Email.Persistence;

public sealed class EmailLog
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public string? HangfireJobId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Queued;
    public string? Error { get; set; }
    public EmailCulture Culture { get; set; } = EmailCulture.En;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string? MessageJson { get; set; }
}
