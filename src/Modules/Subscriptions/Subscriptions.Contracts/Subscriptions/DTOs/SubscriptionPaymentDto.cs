namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record SubscriptionPaymentDto(
    Guid PaymentId,
    string Status,
    decimal Amount,
    string Currency,
    PaymentMethodSnapshotDto? PaymentMethod,
    DateTimeOffset? PaidAt);
