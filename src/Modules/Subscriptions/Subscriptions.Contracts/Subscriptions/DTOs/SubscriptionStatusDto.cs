namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record SubscriptionStatusDto(
    string Status,
    string PlanName,
    DateTimeOffset? RenewalDate,
    DateTimeOffset? GracePeriodEndsAt,
    decimal Price,
    string Currency,
    decimal? UpcomingPrice = null,
    DateTimeOffset? PriceEffectiveAt = null);
