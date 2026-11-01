using Shared.Contracts.Localization;

namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record AdminPlanDto(
    Guid Id,
    LocalizedText Name,
    LocalizedText? Description,
    decimal Price,
    string Currency,
    int BillingCycleInMonths,
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    bool IsActive,
    bool IsPublic,
    long Version,
    int SubscriberCount,
    DateTimeOffset UpdatedAt,
    List<AdminPlanFeatureDto> Features,
    decimal? DiscountPercent = null,
    decimal? UpcomingPrice = null,
    DateTimeOffset? PriceEffectiveAt = null,
    DateTimeOffset? PriceChangeAnnouncedAt = null);
