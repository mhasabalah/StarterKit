using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Coupons.Exceptions;

namespace Subscriptions.Domains.Coupons.Models;

/// <summary>
/// A promotional code. Terms (type, value, duration) are fixed at creation: subscribers snapshot them when they
/// redeem. <see cref="RedemptionCount"/> only changes through <see cref="Redeem"/>; the aggregate's optimistic
/// concurrency <c>Version</c> makes two requests racing for the last redemption safe (the loser gets a 409 and a
/// retry sees <see cref="MaxRedemptions"/> reached).
/// </summary>
public class Coupon : Aggregate<Guid>
{
    public const int MaxCodeLength = 50;

    public string Code { get; private set; } = default!;
    public string? Description { get; private set; }
    public CouponDiscountType DiscountType { get; private set; }

    /// <summary>Percent (0, 100] or a fixed amount in <see cref="Currency"/>.</summary>
    public decimal Value { get; private set; }

    /// <summary>Fixed-amount coupons only: redeemable just on subscriptions in this currency.</summary>
    public string? Currency { get; private set; }

    public CouponDuration Duration { get; private set; }

    /// <summary>Repeating coupons: the number of bills discounted.</summary>
    public int? DurationPeriods { get; private set; }

    public int? MaxRedemptions { get; private set; }
    public int RedemptionCount { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Plans the coupon is valid for; empty = every plan.</summary>
    public List<Guid> AllowedPlanIds { get; private set; } = [];

    public DateTimeOffset UpdatedAt { get; private set; }

    private Coupon()
    {
    }

    /// <summary>Codes are case-insensitive: stored trimmed and upper-cased.</summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static Coupon Create(
        string code,
        string? description,
        CouponDiscountType discountType,
        decimal value,
        string? currency,
        CouponDuration duration,
        int? durationPeriods,
        int? maxRedemptions,
        DateTimeOffset? expiresAt,
        IEnumerable<Guid>? allowedPlanIds,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(code) || NormalizeCode(code).Length > MaxCodeLength)
            throw new BadRequestException($"Code is required and at most {MaxCodeLength} characters.");

        if (discountType == CouponDiscountType.Percent)
        {
            if (value is <= 0m or > 100m)
                throw new BadRequestException("A percent coupon must be greater than 0 and at most 100.");
        }
        else
        {
            if (value <= 0m)
                throw new BadRequestException("A fixed-amount coupon must be greater than 0.");
            if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
                throw new BadRequestException("A fixed-amount coupon needs a 3-letter currency.");
        }

        if (duration == CouponDuration.Repeating && durationPeriods is not > 0)
            throw new BadRequestException("A repeating coupon needs DurationPeriods of at least 1.");

        if (maxRedemptions is < 1)
            throw new BadRequestException("MaxRedemptions must be at least 1 (or empty for unlimited).");

        return new Coupon
        {
            Id = Guid.NewGuid(),
            Code = NormalizeCode(code),
            Description = description,
            DiscountType = discountType,
            Value = value,
            Currency = discountType == CouponDiscountType.FixedAmount ? currency!.Trim().ToUpperInvariant() : null,
            Duration = duration,
            DurationPeriods = duration == CouponDuration.Repeating ? durationPeriods : null,
            MaxRedemptions = maxRedemptions,
            ExpiresAt = expiresAt?.ToUniversalTime(),
            IsActive = true,
            AllowedPlanIds = allowedPlanIds?.Distinct().ToList() ?? [],
            UpdatedAt = now
        };
    }

    public void Update(
        string? description,
        int? maxRedemptions,
        DateTimeOffset? expiresAt,
        IEnumerable<Guid>? allowedPlanIds,
        bool isActive,
        DateTimeOffset now)
    {
        if (maxRedemptions is < 1)
            throw new BadRequestException("MaxRedemptions must be at least 1 (or empty for unlimited).");

        if (maxRedemptions is { } max && max < RedemptionCount)
            throw new ConflictException($"The coupon was already redeemed {RedemptionCount} times; MaxRedemptions cannot be lower.");

        Description = description;
        MaxRedemptions = maxRedemptions;
        ExpiresAt = expiresAt?.ToUniversalTime();
        AllowedPlanIds = allowedPlanIds?.Distinct().ToList() ?? [];
        IsActive = isActive;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    /// <summary>Throws <see cref="InvalidCouponException"/> (with a reason code) unless the coupon can be used now.</summary>
    public void EnsureRedeemable(Guid planId, string currency, DateTimeOffset now)
    {
        if (!IsActive)
            throw new InvalidCouponException(InvalidCouponException.Inactive, "This coupon is not active.");

        if (ExpiresAt is { } expires && expires <= now)
            throw new InvalidCouponException(InvalidCouponException.Expired, "This coupon has expired.");

        if (MaxRedemptions is { } max && RedemptionCount >= max)
            throw new InvalidCouponException(InvalidCouponException.Exhausted, "This coupon has been fully redeemed.");

        if (AllowedPlanIds.Count > 0 && !AllowedPlanIds.Contains(planId))
            throw new InvalidCouponException(InvalidCouponException.PlanNotAllowed, "This coupon is not valid for the selected plan.");

        if (DiscountType == CouponDiscountType.FixedAmount &&
            !string.Equals(Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidCouponException(InvalidCouponException.CurrencyMismatch,
                $"This coupon is only valid for {Currency} subscriptions.");
    }

    /// <summary>The terms a subscription snapshots when this coupon is redeemed.</summary>
    public CouponTerms Terms =>
        new(Id, Code, DiscountType, Value, CouponTerms.InitialPeriods(Duration, DurationPeriods));

    /// <summary>
    /// Counts one redemption and returns its record. Callers add the redemption to the context and save in the same
    /// unit of work (the aggregate version check then serializes racing redemptions).
    /// </summary>
    public CouponRedemption Redeem(
        Guid subscriberId,
        Guid subscriptionId,
        Guid planId,
        string currency,
        DateTimeOffset now)
    {
        EnsureRedeemable(planId, currency, now);

        RedemptionCount++;
        UpdatedAt = now;

        return CouponRedemption.Create(Id, subscriberId, subscriptionId, now);
    }
}
