using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Contracts.Subscriptions.Commands.Coupons;

/// <param name="Value">Percent (0-100, exclusive of 0) or a fixed amount in <paramref name="Currency"/>.</param>
/// <param name="DurationPeriods">Required for <see cref="CouponDuration.Repeating"/>: how many bills are discounted.</param>
/// <param name="AllowedPlanIds">Empty or null = any plan.</param>
public record CreateCouponCommand(
    string Code,
    string? Description,
    CouponDiscountType DiscountType,
    decimal Value,
    string? Currency,
    CouponDuration Duration,
    int? DurationPeriods,
    int? MaxRedemptions,
    DateTimeOffset? ExpiresAt,
    List<Guid>? AllowedPlanIds) : ICommand<Guid>;

/// <summary>Removes a coupon that was never redeemed; a redeemed one can only be deactivated (409).</summary>
public record DeleteCouponCommand(Guid CouponId) : ICommand;

/// <summary>
/// Changes what is safe to change after creation. Discount terms (type, value, duration) are immutable because
/// subscribers snapshot them on redemption.
/// </summary>
public record UpdateCouponCommand(
    Guid CouponId,
    string? Description,
    int? MaxRedemptions,
    DateTimeOffset? ExpiresAt,
    List<Guid>? AllowedPlanIds,
    bool IsActive,
    long? Version = null) : ICommand;
