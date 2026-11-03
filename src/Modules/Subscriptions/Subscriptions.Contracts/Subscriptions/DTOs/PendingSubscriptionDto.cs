namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <param name="Version">The subscription version (round-trip as <c>ApproveSubscriptionCommand.Version</c>).</param>
public record PendingSubscriptionDto(
    Guid SubscriptionId,
    Guid SubscriberId,
    string PlanName,
    decimal Price,
    string Currency,
    DateTimeOffset CreatedAt,
    string LatestPaymentStatus,
    Guid? LatestPaymentId = null,
    long Version = 0);
