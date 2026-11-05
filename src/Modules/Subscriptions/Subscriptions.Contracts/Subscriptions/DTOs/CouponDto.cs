namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record CouponDto(
    Guid Id,
    string Code,
    string? Description,
    string DiscountType,
    decimal Value,
    string? Currency,
    string Duration,
    int? DurationPeriods,
    int? MaxRedemptions,
    int RedemptionCount,
    DateTimeOffset? ExpiresAt,
    bool IsActive,
    List<Guid> AllowedPlanIds,
    long Version,
    DateTimeOffset UpdatedAt);
