namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record PendingPaymentDto(
    Guid PaymentId,
    Guid SubscriptionId,
    Guid SubscriberId,
    decimal Amount,
    string Currency,
    PaymentMethodSnapshotDto? PaymentMethod,
    DateTimeOffset SubmittedAt,
    string Note);
