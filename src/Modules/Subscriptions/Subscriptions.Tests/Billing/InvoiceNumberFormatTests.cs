using FluentAssertions;
using Subscriptions.Domains.Subscriptions.Billing;
using Xunit;

namespace Subscriptions.Tests.Billing;

public class InvoiceNumberFormatTests
{
    [Theory]
    [InlineData("INV", 2026, 1L, "INV-2026-000001")]
    [InlineData("INV", 2026, 42L, "INV-2026-000042")]
    [InlineData("ACME-SA", 2027, 999999L, "ACME-SA-2027-999999")]
    [InlineData("INV", 2026, 1234567L, "INV-2026-1234567")]
    public void Formats_prefix_year_and_zero_padded_sequence(string prefix, int year, long seq, string expected)
    {
        InvoiceNumberFormat.Format(prefix, year, seq).Should().Be(expected);
    }

    [Fact]
    public void Longest_prefix_with_a_ten_digit_sequence_fits_the_column()
    {
        var number = InvoiceNumberFormat.Format(new string('X', InvoiceNumberFormat.MaxPrefixLength), 2026, 9_999_999_999L);

        number.Length.Should().BeLessThanOrEqualTo(InvoiceNumberFormat.MaxLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("THIS-PREFIX-IS-WAY-TOO-LONG")]
    public void Invalid_prefixes_are_rejected(string prefix)
    {
        var act = () => InvoiceNumberFormat.Format(prefix, 2026, 1);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sequence_starts_at_one()
    {
        var act = () => InvoiceNumberFormat.Format("INV", 2026, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
