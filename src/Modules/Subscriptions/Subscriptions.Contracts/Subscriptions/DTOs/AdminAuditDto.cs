namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record AdminAuditDto(
    Guid Id,
    Guid AdminId,
    string Action,
    Guid TargetId,
    string TargetType,
    Guid SubscriberId,
    string? Reason,
    DateTimeOffset OccurredAt);
