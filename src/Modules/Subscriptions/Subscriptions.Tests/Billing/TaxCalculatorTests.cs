using FluentAssertions;
using Subscriptions.Domains.Subscriptions.Billing;
using Xunit;

namespace Subscriptions.Tests.Billing;

public class TaxCalculatorTests
{
    private static readonly TaxPolicy Inclusive15 = new(0.15m, PricesIncludeTax: true);
    private static readonly TaxPolicy Exclusive15 = new(0.15m, PricesIncludeTax: false);

    [Fact]
    public void Inclusive_splits_gross_into_net_and_tax()
    {
        // The old formula net = gross * (1 - r) gave 977.50 / 172.50.
        var result = TaxCalculator.FromPrice(1150m, Inclusive15, "SAR");

        result.Net.Should().Be(1000m);
        result.Tax.Should().Be(150m);
        result.Gross.Should().Be(1150m);
        result.PricesIncludeTax.Should().BeTrue();
    }

    [Fact]
    public void Exclusive_adds_tax_on_top_of_the_net_price()
    {
        var result = TaxCalculator.FromPrice(1000m, Exclusive15, "SAR");

        result.Net.Should().Be(1000m);
        result.Tax.Should().Be(150m);
        result.Gross.Should().Be(1150m);
        result.PricesIncludeTax.Should().BeFalse();
    }

    [Theory]
    [InlineData("99.99", "0.15")]
    [InlineData("0.01", "0.15")]
    [InlineData("10.05", "0.075")]
    [InlineData("123456.78", "0.14")]
    [InlineData("33.33", "0.05")]
    [InlineData("1", "0.333")]
    public void Inclusive_parts_always_sum_exactly_to_the_gross(string gross, string rate)
    {
        var result = TaxCalculator.FromPrice(Parse(gross), new TaxPolicy(Parse(rate), true), "SAR");

        (result.Net + result.Tax).Should().Be(result.Gross);
        result.Gross.Should().Be(Parse(gross));
        decimal.Round(result.Net, 2).Should().Be(result.Net);
        decimal.Round(result.Tax, 2).Should().Be(result.Tax);
    }

    [Theory]
    [InlineData("99.99", "0.15")]
    [InlineData("10.01", "0.15")]
    [InlineData("0.03", "0.075")]
    [InlineData("777.77", "0.14")]
    public void Exclusive_parts_sum_and_back_derivation_from_gross_recovers_the_net(string net, string rate)
    {
        var policy = new TaxPolicy(Parse(rate), false);
        var priced = TaxCalculator.FromPrice(Parse(net), policy, "SAR");
        var fromGross = TaxCalculator.FromGross(priced.Gross, policy, "SAR");

        (priced.Net + priced.Tax).Should().Be(priced.Gross);
        fromGross.Net.Should().Be(priced.Net);
        fromGross.Tax.Should().Be(priced.Tax);
    }

    [Fact]
    public void Rounding_is_away_from_zero_at_the_midpoint()
    {
        // 10.10 * 0.075 = 0.7575 -> 0.76
        var result = TaxCalculator.FromPrice(10.10m, new TaxPolicy(0.075m, false), "SAR");

        result.Tax.Should().Be(0.76m);
        result.Gross.Should().Be(10.86m);
    }

    [Fact]
    public void Seven_and_a_half_percent_is_computed_and_printed_without_truncation()
    {
        var result = TaxCalculator.FromPrice(1075m, new TaxPolicy(0.075m, true), "SAR");

        result.Net.Should().Be(1000m);
        result.Tax.Should().Be(75m);
        TaxCalculator.FormatRate(0.075m).Should().Be("7.5%");
        TaxCalculator.FormatRate(0.15m).Should().Be("15%");
        TaxCalculator.FormatRate(0m).Should().Be("0%");
    }

    [Fact]
    public void Zero_rate_has_no_tax()
    {
        var result = TaxCalculator.FromPrice(500m, new TaxPolicy(0m, false), "SAR");

        result.Tax.Should().Be(0m);
        result.Gross.Should().Be(500m);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("1.01")]
    public void Rate_outside_zero_to_one_is_rejected(string rate)
    {
        var act = () => TaxCalculator.FromPrice(100m, new TaxPolicy(Parse(rate), true), "SAR");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Negative_amounts_split_symmetrically_for_credit_notes()
    {
        var result = TaxCalculator.FromGross(-1150m, Inclusive15, "SAR");

        result.Net.Should().Be(-1000m);
        result.Tax.Should().Be(-150m);
    }

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
