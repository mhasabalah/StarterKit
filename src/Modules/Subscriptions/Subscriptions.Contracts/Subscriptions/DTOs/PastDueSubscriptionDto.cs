namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record PastDueSubscriptionDto(
    Guid SubscriptionId,
    Guid SubscriberId,
    string PlanName,
    DateTimeOffset? GracePeriodEndsAt,
    int DaysUntilExpiry,
    string LatestPaymentStatus);
