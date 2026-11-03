using FluentAssertions;
using Shared.Core.Exceptions;
using Subscriptions.Domains.Subscriptions.Billing;
using Xunit;

namespace Subscriptions.Tests.Billing;

public class CreditNotePolicyTests
{
    private static readonly TaxBreakdown Original = new(1000m, 150m, 1150m, 0.15m, true);
    private static readonly TaxBreakdown NothingCredited = new(0m, 0m, 0m, 0.15m, true);

    [Fact]
    public void Full_credit_mirrors_the_original_exactly_with_negative_amounts()
    {
        var note = CreditNotePolicy.Compute(Original, NothingCredited, null);

        note.Net.Should().Be(-1000m);
        note.Tax.Should().Be(-150m);
        note.Gross.Should().Be(-1150m);
        note.Rate.Should().Be(0.15m);
    }

    [Fact]
    public void Partial_credit_splits_with_the_original_rate()
    {
        var note = CreditNotePolicy.Compute(Original, NothingCredited, 115m);

        note.Net.Should().Be(-100m);
        note.Tax.Should().Be(-15m);
        note.Gross.Should().Be(-115m);
    }

    [Fact]
    public void Closing_credit_takes_the_exact_remainder_so_parts_sum_to_the_original()
    {
        var odd = new TaxBreakdown(86.95m, 13.05m, 100m, 0.15m, true);
        var first = CreditNotePolicy.Compute(odd, NothingCredited, 33.33m);
        var credited = new TaxBreakdown(-first.Net, -first.Tax, -first.Gross, 0.15m, true);

        var second = CreditNotePolicy.Compute(odd, credited, null);

        (first.Net + second.Net).Should().Be(-86.95m);
        (first.Tax + second.Tax).Should().Be(-13.05m);
        (first.Gross + second.Gross).Should().Be(-100m);
    }

    [Fact]
    public void Credit_above_the_remainder_is_rejected()
    {
        var credited = new TaxBreakdown(800m, 120m, 920m, 0.15m, true);

        var act = () => CreditNotePolicy.Compute(Original, credited, 230.01m);

        act.Should().Throw<BadRequestException>();
        CreditNotePolicy.Creditable(1150m, 920m).Should().Be(230m);
    }

    [Fact]
    public void Fully_credited_invoice_cannot_be_credited_again()
    {
        var act = () => CreditNotePolicy.Compute(Original, Original, null);

        act.Should().Throw<ConflictException>();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.001")]
    public void Invalid_amounts_are_rejected(string amount)
    {
        var act = () => CreditNotePolicy.Compute(
            Original, NothingCredited, decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));

        act.Should().Throw<BadRequestException>();
    }
}
