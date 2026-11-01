namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record RenewalStatusDto(
    Guid SubscriptionId,
    string Status,
    DateTimeOffset? GracePeriodEndsAt,
    int DaysUntilExpiry,
    Guid LatestPaymentId,
    string LatestPaymentStatus,
    decimal Amount,
    string Currency,
    decimal ExpectedAmount);
