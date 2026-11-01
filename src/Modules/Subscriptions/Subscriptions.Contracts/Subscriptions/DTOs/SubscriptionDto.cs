namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record SubscriptionDto(
    Guid SubscriptionId,
    string PlanName,
    string Status,
    decimal Price,
    string Currency,
    int BillingCycleInMonths,
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    DateTimeOffset? StartDate,
    DateTimeOffset? RenewalDate,
    DateTimeOffset? GracePeriodEndsAt,
    SubscriptionPaymentDto? LatestPayment,
    ScheduledPlanChangeDto? ScheduledPlanChange = null,
    bool CancelledAtPeriodEnd = false,
    DateTimeOffset? ScheduledCancellationDate = null,
    string? CancellationReason = null,
    decimal? UpcomingPrice = null,
    DateTimeOffset? PriceEffectiveAt = null);
