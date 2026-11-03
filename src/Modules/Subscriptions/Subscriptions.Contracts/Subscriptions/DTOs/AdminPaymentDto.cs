namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record AdminPaymentDto(
    Guid PaymentId,
    Guid SubscriptionId,
    Guid SubscriberId,
    decimal Amount,
    string Currency,
    string Status,
    PaymentMethodSnapshotDto? PaymentMethod,
    DateTimeOffset? PaidAt,
    DateTimeOffset CreatedAt,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? Notes,
    string Purpose = "",
    long Version = 0);
