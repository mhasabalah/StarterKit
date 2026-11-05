using FluentAssertions;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Xunit;

namespace Subscriptions.Tests;

public class StripeAmountsTests
{
    [Theory]
    [InlineData("KWD", 12.345, 12345)]
    [InlineData("BHD", 0.001, 1)]
    [InlineData("OMR", 5.5, 5500)]
    [InlineData("JOD", 1.234, 1234)]
    [InlineData("TND", 2, 2000)]
    [InlineData("LYD", 9.999, 9999)]
    [InlineData("IQD", 1.5, 1500)]
    [InlineData("JPY", 1000, 1000)]
    [InlineData("KRW", 5000, 5000)]
    [InlineData("SAR", 10.25, 1025)]
    [InlineData("usd", 0.01, 1)]
    public void ToMinorUnits_uses_the_currency_exponent(string currency, double amount, long minor)
    {
        StripeAmounts.ToMinorUnits((decimal)amount, currency).Should().Be(minor);
    }

    [Theory]
    [InlineData("KWD", 12345, 12.345)]
    [InlineData("BHD", 1, 0.001)]
    [InlineData("OMR", 5500, 5.5)]
    [InlineData("JOD", 1234, 1.234)]
    [InlineData("TND", 2000, 2)]
    [InlineData("LYD", 9999, 9.999)]
    [InlineData("IQD", 1500, 1.5)]
    [InlineData("JPY", 1000, 1000)]
    [InlineData("KRW", 5000, 5000)]
    [InlineData("SAR", 1025, 10.25)]
    public void FromMinorUnits_keeps_every_decimal_of_the_currency(string currency, long minor, double amount)
    {
        StripeAmounts.FromMinorUnits(minor, currency).Should().Be((decimal)amount);
    }
}
