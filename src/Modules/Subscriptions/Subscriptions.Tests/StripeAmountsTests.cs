using FluentAssertions;
using Stripe;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Xunit;

namespace Subscriptions.Tests;

/// <summary>Stripe's own minor-unit rules (https://docs.stripe.com/currencies), which differ from ISO 4217.</summary>
public class StripeAmountsTests
{
    [Theory]
    [InlineData("KWD", 12.345, 12345)]
    [InlineData("BHD", 0.001, 1)]
    [InlineData("OMR", 5.5, 5500)]
    [InlineData("JOD", 1.234, 1234)]
    [InlineData("JPY", 1000, 1000)]
    [InlineData("KRW", 5000, 5000)]
    [InlineData("MGA", 1000, 1000)] // two-decimal in ISO, zero-decimal at Stripe
    [InlineData("ISK", 100, 10000)] // zero-decimal in ISO, sent x100 at Stripe
    [InlineData("UGX", 5000, 500000)] // same special case as ISK
    [InlineData("HUF", 1500, 150000)] // two-decimal in the API
    [InlineData("TWD", 300, 30000)]
    [InlineData("SAR", 10.25, 1025)]
    [InlineData("usd", 0.01, 1)]
    public void ToMinorUnits_follows_stripes_table(string currency, double amount, long minor) =>
        StripeAmounts.ToMinorUnits((decimal)amount, currency).Should().Be(minor);

    [Theory]
    [InlineData("KWD", 12345, 12.345)]
    [InlineData("BHD", 1, 0.001)]
    [InlineData("MGA", 1000, 1000)]
    [InlineData("ISK", 10000, 100)]
    [InlineData("UGX", 500000, 5000)]
    [InlineData("JPY", 1000, 1000)]
    [InlineData("HUF", 150000, 1500)]
    [InlineData("SAR", 1025, 10.25)]
    public void FromMinorUnits_inverts_it(string currency, long minor, double amount) =>
        StripeAmounts.FromMinorUnits(minor, currency).Should().Be((decimal)amount);

    [Theory]
    [InlineData("KWD", "12.340", true)]
    [InlineData("KWD", "12.345", false)] // three-decimal: the last minor digit must be 0
    [InlineData("BHD", "0.010", true)]
    [InlineData("OMR", "5.001", false)]
    [InlineData("IQD", "10.000", false)] // three decimals in ISO, not offered by Stripe
    [InlineData("LYD", "10.000", false)]
    [InlineData("TND", "10.000", false)]
    [InlineData("JPY", "1000", true)]
    [InlineData("JPY", "1000.5", false)]
    [InlineData("MGA", "1000", true)]
    [InlineData("MGA", "1000.50", false)]
    [InlineData("ISK", "100", true)]
    [InlineData("HUF", "1500", true)]
    [InlineData("HUF", "1500.50", false)] // payouts need whole units
    [InlineData("TWD", "300.5", false)]
    [InlineData("USD", "10.25", true)]
    [InlineData("USD", "10.255", false)]
    public void IsChargeable_applies_the_documented_restrictions(string currency, string amount, bool chargeable) =>
        StripeAmounts.IsChargeable(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency)
            .Should().Be(chargeable);

    private static StripeException Error(string type, string? code = null) =>
        new("stripe error") { StripeError = new StripeError { Type = type, Code = code } };

    [Fact]
    public void Card_errors_split_into_authentication_required_and_declined()
    {
        StripePaymentGateway.ClassifyChargeError(Error("card_error", "authentication_required"))
            .Status.Should().Be(OffSessionChargeStatus.RequiresAction);

        var declined = StripePaymentGateway.ClassifyChargeError(Error("card_error", "card_declined"));
        declined.Status.Should().Be(OffSessionChargeStatus.Declined);
        declined.FailureCode.Should().Be("card_declined");
    }

    [Fact]
    public void Invalid_request_is_declined_with_a_distinct_failure_code()
    {
        var result = StripePaymentGateway.ClassifyChargeError(Error("invalid_request_error", "resource_missing"));

        result.Status.Should().Be(OffSessionChargeStatus.Declined);
        result.FailureCode.Should().Be(StripePaymentGateway.InvalidRequestPrefix + "resource_missing");
    }

    [Theory]
    [InlineData("api_error")]
    [InlineData("api_connection_error")]
    [InlineData("rate_limit_error")]
    [InlineData("idempotency_error")]
    public void Provider_problems_are_transient(string type) =>
        StripePaymentGateway.ClassifyChargeError(Error(type)).Status.Should().Be(OffSessionChargeStatus.Transient);

    [Fact]
    public void An_error_without_a_body_is_transient() =>
        StripePaymentGateway.ClassifyChargeError(new StripeException("network")).Status
            .Should().Be(OffSessionChargeStatus.Transient);
}
