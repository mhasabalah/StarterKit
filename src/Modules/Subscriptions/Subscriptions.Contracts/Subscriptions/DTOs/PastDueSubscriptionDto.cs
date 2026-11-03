namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <param name="Version">The subscription version (round-trip as <c>ApproveRenewalCommand.Version</c>).</param>
public record PastDueSubscriptionDto(
    Guid SubscriptionId,
    Guid SubscriberId,
    string PlanName,
    DateTimeOffset? GracePeriodEndsAt,
    int DaysUntilExpiry,
    string LatestPaymentStatus,
    Guid? RenewalPaymentId = null,
    decimal? RenewalAmount = null,
    long Version = 0);
