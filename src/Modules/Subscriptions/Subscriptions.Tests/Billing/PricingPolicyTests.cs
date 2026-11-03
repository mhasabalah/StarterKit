using FluentAssertions;
using Shared.Contracts.Localization;
using Subscriptions.Domains.Plans.Models;
using Subscriptions.Domains.Subscriptions.Billing;
using Xunit;

namespace Subscriptions.Tests.Billing;

public class PricingPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Plan_price_uses_the_pending_price_once_effective()
    {
        PricingPolicy.PlanPriceAt(100m, 120m, Now, null, Now).Should().Be(120m);
        PricingPolicy.PlanPriceAt(100m, 120m, Now.AddDays(1), null, Now).Should().Be(100m);
        PricingPolicy.PlanPriceAt(100m, null, null, null, Now).Should().Be(100m);
    }

    [Fact]
    public void Plan_price_applies_the_discount_after_the_price_change()
    {
        PricingPolicy.PlanPriceAt(100m, null, null, 20m, Now).Should().Be(80m);
        PricingPolicy.PlanPriceAt(100m, 200m, Now, 10m, Now).Should().Be(180m);
        PricingPolicy.PlanPriceAt(99.99m, null, null, 33.33m, Now).Should().Be(66.66m);
        PricingPolicy.PlanPriceAt(100m, null, null, 0m, Now).Should().Be(100m);
        PricingPolicy.PlanPriceAt(100m, null, null, 150m, Now).Should().Be(0m);
    }

    [Fact]
    public void Renewal_charge_adds_deferred_proration_and_tax()
    {
        var charge = PricingPolicy.RenewalCharge(1000m, 50m, new TaxPolicy(0.15m, PricesIncludeTax: false));

        charge.PeriodPrice.Should().Be(1000m);
        charge.DeferredProration.Should().Be(50m);
        charge.AmountDue.Should().Be(1207.50m);
    }

    [Fact]
    public void Renewal_charge_with_inclusive_prices_is_the_price_plus_deferred()
    {
        var charge = PricingPolicy.RenewalCharge(1150m, 0m, new TaxPolicy(0.15m, PricesIncludeTax: true));

        charge.AmountDue.Should().Be(1150m);
    }

    [Fact]
    public void Renewal_period_price_follows_the_plan_but_never_across_currency_or_cycle()
    {
        var subscription = SubscriptionFactory.Active(price: 100m, currency: "SAR", cycle: 1, now: Now.AddMonths(-1));
        var samePlan = Plan.Create(LocalizedText.Create("P", "P"), null, 130m, "SAR", 1, null, null, null, true);
        var otherCurrency = Plan.Create(LocalizedText.Create("P", "P"), null, 130m, "USD", 1, null, null, null, true);
        var otherCycle = Plan.Create(LocalizedText.Create("P", "P"), null, 130m, "SAR", 12, null, null, null, true);

        PricingPolicy.RenewalPeriodPrice(subscription, samePlan, Now).Should().Be(130m);
        PricingPolicy.RenewalPeriodPrice(subscription, otherCurrency, Now).Should().Be(100m);
        PricingPolicy.RenewalPeriodPrice(subscription, otherCycle, Now).Should().Be(100m);
        PricingPolicy.RenewalPeriodPrice(subscription, null, Now).Should().Be(100m);
    }

    [Fact]
    public void Upgrade_proration_covers_only_the_remaining_part_of_the_current_period()
    {
        var start = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero); // 28 days
        var at = new DateTimeOffset(2026, 2, 8, 0, 0, 0, TimeSpan.Zero);  // 21 days left

        PricingPolicy.UpgradeProration(100m, 200m, start, end, at).Should().Be(75m);
    }

    [Fact]
    public void Upgrade_proration_after_renewals_uses_the_current_period_not_the_lifetime()
    {
        // Subscribed a year ago; the current monthly period started 10 days ago and has 20 days left (30-day month).
        var periodStart = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        var at = new DateTimeOffset(2026, 4, 11, 0, 0, 0, TimeSpan.Zero);

        PricingPolicy.UpgradeProration(90m, 120m, periodStart, periodEnd, at).Should().Be(20m);
    }

    [Theory]
    [InlineData("100", "100")]
    [InlineData("100", "80")]
    public void Upgrade_proration_is_zero_when_the_price_does_not_rise(string current, string next)
    {
        var start = Now;
        PricingPolicy.UpgradeProration(decimal.Parse(current), decimal.Parse(next), start, start.AddMonths(1), start)
            .Should().Be(0m);
    }

    [Fact]
    public void Upgrade_proration_is_zero_at_or_after_the_period_end()
    {
        PricingPolicy.UpgradeProration(100m, 200m, Now, Now.AddMonths(1), Now.AddMonths(1)).Should().Be(0m);
    }

    [Theory]
    [InlineData(true, "15", true)]
    [InlineData(true, "15.01", false)]
    [InlineData(false, "1", false)]
    [InlineData(true, "0", false)]
    public void Deferral_only_for_trusted_subscribers_under_the_threshold(bool trusted, string proration, bool expected)
    {
        PricingPolicy.ShouldDeferProration(trusted, decimal.Parse(proration, System.Globalization.CultureInfo.InvariantCulture), 100m, 15)
            .Should().Be(expected);
    }
}
