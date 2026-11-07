using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>
/// The coupon terms a subscription carries after redemption (a snapshot: later edits or deactivation of the coupon never
/// change what the subscriber was promised).
/// </summary>
/// <param name="PeriodsRemaining">Bills still to be discounted; null = forever.</param>
public sealed record CouponTerms(
    Guid CouponId,
    string Code,
    CouponDiscountType Type,
    decimal Value,
    int? PeriodsRemaining)
{
    /// <summary>Bills a freshly redeemed coupon discounts: Once = 1, Repeating = n, Forever = unlimited (null).</summary>
    public static int? InitialPeriods(CouponDuration duration, int? durationPeriods) => duration switch
    {
        CouponDuration.Once => 1,
        CouponDuration.Repeating => durationPeriods ?? 1,
        _ => null
    };
}

/// <summary>
/// Pure coupon math, applied to a period price (tax basis) BEFORE tax, after the plan's own <c>DiscountPercent</c>.
/// A coupon never brings a charge to zero (the smallest charge is one minor unit, <see cref="MinimumChargeFor"/>): zero-amount payments
/// are reserved for complimentary activations, and the checkout/settlement path expects a positive amount.
/// </summary>
public static class CouponPricing
{
    /// <summary>The smallest charge a coupon leaves: one minor unit of the currency (0.01 USD, 1 JPY, 0.001 KWD).</summary>
    public static decimal MinimumChargeFor(string currency) => Currency.SmallestUnit(currency);

    public static decimal Discount(decimal price, CouponDiscountType type, decimal value, string currency)
    {
        var minimum = MinimumChargeFor(currency);
        if (price <= minimum || value <= 0m)
            return 0m;

        var raw = type == CouponDiscountType.Percent
            ? price * Math.Min(value, 100m) / 100m
            : value;

        return Math.Min(Currency.Round(raw, currency), price - minimum);
    }

    public static decimal Discount(decimal price, CouponTerms? coupon, string currency) =>
        coupon is null ? 0m : Discount(price, coupon.Type, coupon.Value, currency);
}
