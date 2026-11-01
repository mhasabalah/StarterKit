namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record RefundRequestDto(
    Guid RefundRequestId,
    Guid SubscriptionId,
    string PlanName,
    decimal RequestedAmount,
    string Currency,
    string RefundType,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ProcessedAt,
    string? ExternalTransactionRef);
