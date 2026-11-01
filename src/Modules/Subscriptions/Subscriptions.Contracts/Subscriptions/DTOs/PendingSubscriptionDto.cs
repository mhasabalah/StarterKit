namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record PendingSubscriptionDto(
    Guid SubscriptionId,
    Guid SubscriberId,
    string PlanName,
    decimal Price,
    string Currency,
    DateTimeOffset CreatedAt,
    string LatestPaymentStatus);
