namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>What a renewal bills: the period price plus any deferred upgrade proration, and the gross amount due.</summary>
/// <param name="PeriodPrice">Price of the new period (tax basis), becomes the subscription's price snapshot.</param>
/// <param name="DeferredProration">Deferred upgrade proration included in this bill (tax basis).</param>
/// <param name="AmountDue">Gross amount of the renewal payment.</param>
/// <param name="CouponDiscount">Coupon discount taken off <paramref name="PeriodPrice"/> (tax basis); 0 = none.</param>
/// <param name="CouponCode">The coupon behind <paramref name="CouponDiscount"/>.</param>
public readonly record struct RenewalCharge(
    decimal PeriodPrice,
    decimal DeferredProration,
    decimal AmountDue,
    decimal CouponDiscount = 0m,
    string? CouponCode = null);

/// <summary>
/// The ONE place prices are derived (previously duplicated in the past-due job, renewal approval and renewal
/// status). Pure: callers pass the plan/subscription values and the moment of evaluation.
/// </summary>
public static class PricingPolicy
{
    /// <summary>
    /// The plan's effective list price at <paramref name="at"/>: a scheduled price once its effective date has passed,
    /// then the plan discount (<c>DiscountPercent</c>), rounded to the currency's decimals.
    /// </summary>
    public static decimal PlanPriceAt(
        decimal price,
        decimal? pendingPrice,
        DateTimeOffset? pendingEffectiveAt,
        decimal? discountPercent,
        DateTimeOffset at,
        string? currency = null)
    {
        var basePrice = pendingPrice is { } pending && pendingEffectiveAt is { } effectiveAt && effectiveAt <= at
            ? pending
            : price;

        if (discountPercent is not { } discount || discount <= 0m)
        {
            return Currency.Round(basePrice, currency);
        }

        var clamped = Math.Min(discount, 100m);
        return Currency.Round(basePrice * (1m - clamped / 100m), currency);
    }

    public static decimal PlanPriceAt(Plan plan, DateTimeOffset at) =>
        PlanPriceAt(plan.Price, plan.PendingPrice, plan.PriceEffectiveAt, plan.DiscountPercent, at, plan.Currency);

    /// <summary>
    /// Price of the next period for a subscription renewing at <paramref name="at"/>. Follows the plan's effective
    /// price when the plan is still usable for this subscription (same currency and billing cycle, not deleted);
    /// otherwise keeps the subscription's own snapshot (never mixes currencies).
    /// </summary>
    public static decimal RenewalPeriodPrice(Subscription subscription, Plan? plan, DateTimeOffset at)
    {
        if (plan is null ||
            plan.IsDeleted ||
            !string.Equals(plan.Currency, subscription.Currency, StringComparison.OrdinalIgnoreCase) ||
            plan.BillingCycleInMonths != subscription.BillingCycleInMonths)
        {
            return subscription.Price;
        }

        return PlanPriceAt(plan, at);
    }

    /// <param name="coupon">The subscription's active coupon, if any; discounts the period price (not the deferred proration).</param>
    public static RenewalCharge RenewalCharge(
        decimal periodPrice,
        decimal deferredProration,
        TaxPolicy tax,
        CouponTerms? coupon = null,
        string? currency = null)
    {
        var deferred = Math.Max(0m, deferredProration);
        var discount = CouponPricing.Discount(periodPrice, coupon, currency);
        return new RenewalCharge(
            periodPrice,
            deferred,
            TaxCalculator.AmountDue(periodPrice - discount + deferred, tax, currency),
            discount,
            discount > 0m ? coupon!.Code : null);
    }

    /// <summary>
    /// Gross amount of the FIRST bill of a new subscription: the plan's effective price less the coupon discount.
    /// </summary>
    public static (decimal AmountDue, decimal CouponDiscount) ActivationCharge(
        decimal planPrice,
        TaxPolicy tax,
        CouponTerms? coupon = null,
        string? currency = null)
    {
        var discount = CouponPricing.Discount(planPrice, coupon, currency);
        return (TaxCalculator.AmountDue(planPrice - discount, tax, currency), discount);
    }

    /// <summary>
    /// Prorated price difference for upgrading at <paramref name="at"/> for the rest of the CURRENT period
    /// [<paramref name="periodStart"/>, <paramref name="periodEnd"/>). Zero when nothing is left or the price does not rise.
    /// </summary>
    public static decimal UpgradeProration(
        decimal currentPrice,
        decimal newPrice,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        DateTimeOffset at,
        string? currency = null)
    {
        var difference = newPrice - currentPrice;
        if (difference <= 0m)
        {
            return 0m;
        }

        var fraction = BillingPeriod.RemainingFraction(periodStart, periodEnd, at);
        return Currency.Round(difference * fraction, currency);
    }

    /// <summary>Trusted subscribers may defer small prorations (≤ threshold % of the current price) to the next renewal.</summary>
    public static bool ShouldDeferProration(bool isTrusted, decimal proration, decimal currentPrice, int thresholdPercent) =>
        isTrusted && proration > 0m && proration <= currentPrice * thresholdPercent / 100m;
}
