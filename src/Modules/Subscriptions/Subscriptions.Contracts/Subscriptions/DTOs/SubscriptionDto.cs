namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <param name="CurrentPeriodStart">Start of the current billing period (the previous renewal date).</param>
/// <param name="DeferredProrationAmount">Upgrade proration added to the next renewal (tax basis).</param>
/// <param name="Version">The subscription version (optimistic concurrency).</param>
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
    DateTimeOffset? PriceEffectiveAt = null,
    DateTimeOffset? CurrentPeriodStart = null,
    decimal DeferredProrationAmount = 0m,
    long Version = 0);
