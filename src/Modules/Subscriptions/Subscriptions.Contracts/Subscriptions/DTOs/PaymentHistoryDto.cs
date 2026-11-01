namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record PaymentHistoryDto(
    Guid PaymentId,
    decimal Amount,
    string Currency,
    string Status,
    PaymentMethodSnapshotDto? PaymentMethod,
    DateTimeOffset? PaidAt,
    DateTimeOffset CreatedAt);
