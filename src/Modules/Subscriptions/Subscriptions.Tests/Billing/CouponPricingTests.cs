using FluentAssertions;
using Xunit;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Billing;

namespace Subscriptions.Tests.Billing;

public class CouponPricingTests
{
    private static CouponTerms Terms(CouponDiscountType type, decimal value, int? periods = null) =>
        new(Guid.NewGuid(), "SAVE", type, value, periods);

    [Theory]
    [InlineData(100, 20, 20)]
    [InlineData(99.99, 33.33, 33.33)]
    [InlineData(100, 100, 99.99)] // never brings a charge to zero
    public void Percent_discount_is_rounded_and_leaves_at_least_the_minimum_charge(double price, double percent, double expected) =>
        CouponPricing.Discount((decimal)price, CouponDiscountType.Percent, (decimal)percent).Should().Be((decimal)expected);

    [Theory]
    [InlineData(100, 30, 30)]
    [InlineData(100, 250, 99.99)] // larger than the price: capped
    public void Fixed_discount_is_capped_below_the_price(double price, double amount, double expected) =>
        CouponPricing.Discount((decimal)price, CouponDiscountType.FixedAmount, (decimal)amount).Should().Be((decimal)expected);

    [Fact]
    public void No_coupon_or_a_free_price_means_no_discount()
    {
        CouponPricing.Discount(100m, null).Should().Be(0m);
        CouponPricing.Discount(0m, CouponDiscountType.Percent, 50m).Should().Be(0m);
    }

    [Fact]
    public void Activation_charge_applies_the_coupon_before_tax()
    {
        var tax = new TaxPolicy(0.15m, PricesIncludeTax: false);

        var (amountDue, discount) = PricingPolicy.ActivationCharge(100m, tax, Terms(CouponDiscountType.Percent, 20m));

        discount.Should().Be(20m);
        amountDue.Should().Be(92m, "80 net + 15% tax");
    }

    [Fact]
    public void Renewal_charge_discounts_the_period_price_but_not_the_deferred_proration()
    {
        var tax = new TaxPolicy(0.15m, PricesIncludeTax: false);

        var charge = PricingPolicy.RenewalCharge(100m, 10m, tax, Terms(CouponDiscountType.FixedAmount, 25m));

        charge.PeriodPrice.Should().Be(100m, "the snapshot price stays the list price");
        charge.CouponDiscount.Should().Be(25m);
        charge.CouponCode.Should().Be("SAVE");
        charge.AmountDue.Should().Be(97.75m, "(100 - 25 + 10) net + 15% tax");
    }

    [Fact]
    public void Renewal_charge_without_a_coupon_is_unchanged()
    {
        var charge = PricingPolicy.RenewalCharge(100m, 0m, new TaxPolicy(0.15m, PricesIncludeTax: true));

        charge.AmountDue.Should().Be(100m);
        charge.CouponDiscount.Should().Be(0m);
        charge.CouponCode.Should().BeNull();
    }

    [Theory]
    [InlineData(CouponDuration.Once, null, 1)]
    [InlineData(CouponDuration.Repeating, 3, 3)]
    [InlineData(CouponDuration.Forever, null, null)]
    public void Initial_periods_follow_the_duration(CouponDuration duration, int? periods, int? expected) =>
        CouponTerms.InitialPeriods(duration, periods).Should().Be(expected);
}
