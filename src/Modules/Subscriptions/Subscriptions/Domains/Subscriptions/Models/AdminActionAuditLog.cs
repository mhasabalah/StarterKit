namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>Append-only record of an admin financial action in the Subscriptions module.</summary>
public class AdminActionAuditLog
{
    public Guid Id { get; private set; }
    public Guid AdminId { get; private set; }
    public string Action { get; private set; } = default!;
    public Guid TargetId { get; private set; }
    public string TargetType { get; private set; } = default!;
    public Guid SubscriberId { get; private set; }
    public string? Reason { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Currency { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
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
        string? reason,
        decimal? amount = null,
        string? currency = null,
        string? beforeJson = null,
        string? afterJson = null)
    {
        return new AdminActionAuditLog
        {
            Id = Guid.NewGuid(),
            AdminId = adminId,
            Action = action,
            TargetId = targetId,
            TargetType = targetType,
            SubscriberId = subscriberId,
            Reason = reason is { Length: > 1000 } ? reason[..1000] : reason,
            Amount = amount,
            Currency = currency,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }
}
