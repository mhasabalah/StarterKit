namespace Subscriptions.Domains.Subscriptions.Models;

public class AdminActionAuditLog
{
    public Guid Id { get; private set; }
    public Guid AdminId { get; private set; }
    public string Action { get; private set; } = default!;
    public Guid TargetId { get; private set; }
    public string TargetType { get; private set; } = default!;
    public Guid SubscriberId { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private AdminActionAuditLog()
    {
    }

    public static AdminActionAuditLog Create(
        Guid adminId,
        string action,
        Guid targetId,
        string targetType,
        Guid subscriberId,
        string? reason)
    {
        return new AdminActionAuditLog
        {
            Id = Guid.NewGuid(),
            AdminId = adminId,
            Action = action,
            TargetId = targetId,
            TargetType = targetType,
            SubscriberId = subscriberId,
            Reason = reason,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }
}
