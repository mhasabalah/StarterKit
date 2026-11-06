using System.Globalization;
using FluentAssertions;
using Subscriptions.Contracts.Subscriptions.Enums;
using Subscriptions.Domains.Subscriptions.Billing;
using Xunit;

namespace Subscriptions.Tests.Billing;

public class CurrencyTests
{
    [Theory]
    [InlineData("USD", 2)]
    [InlineData("SAR", 2)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    [InlineData("JPY", 0)]
    [InlineData("KRW", 0)]
    [InlineData("CLF", 4)]
    [InlineData("usd", 2)]
    [InlineData(null, 2)]
    public void Exponent_follows_iso_4217(string? code, int exponent) => Currency.Exponent(code).Should().Be(exponent);

    [Theory]
    [InlineData("KWD", true)]
    [InlineData("jpy", true)]
    [InlineData("USD", true)]
    [InlineData("XXX", false)]
    [InlineData("US", false)]
    [InlineData("DOLLAR", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_accepts_only_billable_iso_codes(string? code, bool known) => Currency.IsKnown(code).Should().Be(known);

    [Theory]
    [InlineData("USD", "10.005", "10.01")]
    [InlineData("USD", "-10.005", "-10.01")]
    [InlineData("KWD", "10.0005", "10.001")]
    [InlineData("JPY", "100.5", "101")]
    [InlineData("JPY", "99.4", "99")]
    [InlineData("CLF", "1.23455", "1.2346")]
    public void Round_uses_the_currency_decimals_away_from_zero(string code, string value, string expected) =>
        Currency.Round(decimal.Parse(value, CultureInfo.InvariantCulture), code)
            .Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("USD", "10.01", true)]
    [InlineData("USD", "10.001", false)]
    [InlineData("KWD", "10.001", true)]
    [InlineData("KWD", "10.0001", false)]
    [InlineData("JPY", "100", true)]
    [InlineData("JPY", "100.5", false)]
    [InlineData("CLF", "1.2345", true)]
    public void HasValidScale_rejects_extra_decimals(string code, string value, bool valid) =>
        Currency.HasValidScale(decimal.Parse(value, CultureInfo.InvariantCulture), code).Should().Be(valid);

    [Theory]
    [InlineData("USD", "12.34", 1234)]
    [InlineData("KWD", "12.345", 12345)]
    [InlineData("JPY", "1000", 1000)]
    [InlineData("CLF", "1.2345", 12345)]
    public void Minor_units_round_trip(string code, string value, long minor)
    {
        var amount = decimal.Parse(value, CultureInfo.InvariantCulture);

        Currency.ToMinorUnits(amount, code).Should().Be(minor);
        Currency.FromMinorUnits(minor, code).Should().Be(amount);
    }

    [Theory]
    [InlineData("USD", "0.01")]
    [InlineData("KWD", "0.001")]
    [InlineData("JPY", "1")]
    public void SmallestUnit_is_one_minor_unit(string code, string expected) =>
        Currency.SmallestUnit(code).Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));

    [Fact]
    public void Format_keeps_the_currency_decimals_and_is_culture_invariant_by_default()
    {
        Currency.Format(10m, "KWD").Should().Be("10.000");
        Currency.Format(10.5m, "JPY").Should().Be("11");
        Currency.Format(1234.5m, "USD").Should().Be("1234.50");
        Currency.FormatWithCode(1.5m, "usd").Should().Be("1.50 USD");
        Currency.FormatLocalized(1.5m, "USD", CultureInfo.GetCultureInfo("de-DE")).Should().Be("1,50");
    }

    [Fact]
    public void Tax_is_computed_and_rounded_in_the_currency_of_the_amount()
    {
        var exclusive = new TaxPolicy(0.15m, PricesIncludeTax: false);

        var kwd = TaxCalculator.FromPrice(10.123m, exclusive, "KWD");
        var jpy = TaxCalculator.FromPrice(1000m, exclusive, "JPY");

        kwd.Net.Should().Be(10.123m);
        kwd.Tax.Should().Be(1.518m); // 1.51845 -> 3 decimals
        kwd.Gross.Should().Be(11.641m);
        jpy.Tax.Should().Be(150m);
        TaxCalculator.FromPrice(10.005m, exclusive).Net.Should().Be(10.01m, "no currency keeps the legacy 2 decimals");
    }

    [Fact]
    public void A_coupon_never_brings_a_charge_below_one_minor_unit()
    {
        CouponPricing.Discount(10m, CouponDiscountType.FixedAmount, 50m, "JPY").Should().Be(9m);
        CouponPricing.Discount(1m, CouponDiscountType.FixedAmount, 50m, "KWD").Should().Be(0.999m);
        CouponPricing.Discount(1m, CouponDiscountType.FixedAmount, 50m, "USD").Should().Be(0.99m);
    }

    [Fact]
    public void Credit_note_amounts_are_checked_against_the_currency_scale()
    {
        var original = new TaxBreakdown(1000m, 150m, 1150m, 0.15m, true);
        var nothing = new TaxBreakdown(0, 0, 0, 0.15m, true);

        var tooPrecise = () => CreditNotePolicy.Compute(original, nothing, 10.5m, "JPY");
        var fine = () => CreditNotePolicy.Compute(original, nothing, 10.123m, "KWD");

        tooPrecise.Should().Throw<Exception>();
        fine.Should().NotThrow();
    }
}
