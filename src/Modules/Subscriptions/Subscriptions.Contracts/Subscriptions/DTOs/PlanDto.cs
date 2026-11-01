namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record PlanDto(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    int BillingCycleInMonths,
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    List<PlanFeatureDto> Features,
    decimal? DiscountPercent = null,
    decimal? UpcomingPrice = null,
    DateTimeOffset? PriceEffectiveAt = null);
