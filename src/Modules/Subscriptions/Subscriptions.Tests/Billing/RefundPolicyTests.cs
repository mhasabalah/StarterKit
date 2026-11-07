using FluentAssertions;
using Shared.Core.Exceptions;
using Subscriptions.Domains.Subscriptions.Billing;
using Subscriptions.Domains.Subscriptions.Models;
using Xunit;

namespace Subscriptions.Tests.Billing;

public class RefundPolicyTests
{
    private static readonly DateTimeOffset PeriodStart = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero); // 365 days

    private static RefundInput Input(decimal periodPaid, decimal refundable, int serviceDays) => new(
        periodPaid, refundable, PeriodStart, PeriodEnd, PeriodStart.AddDays(serviceDays), 14, 45, "SAR");

    [Fact]
    public void Refundable_is_paid_minus_refunded_and_never_negative()
    {
        RefundPolicy.Refundable([1150m, 200m], [300m]).Should().Be(1050m);
        RefundPolicy.Refundable([100m], [100m, 50m]).Should().Be(0m);
        RefundPolicy.Refundable([], []).Should().Be(0m);
    }

    [Fact]
    public void Multiple_payments_add_up()
    {
        RefundPolicy.Refundable([1000m, 1000m, 150m], []).Should().Be(2150m);
    }

    [Fact]
    public void Full_refund_within_the_full_window_is_the_money_received()
    {
        var quote = RefundPolicy.Quote(Input(periodPaid: 1150m, refundable: 1150m, serviceDays: 10));

        quote.Type.Should().Be(RefundType.Full);
        quote.Amount.Should().Be(1150m);
    }

    [Fact]
    public void Prorated_refund_uses_unused_days_over_the_actual_period_length()
    {
        // Service ended after 20 days: 345 of 365 days unused.
        var quote = RefundPolicy.Quote(Input(periodPaid: 1095m, refundable: 1095m, serviceDays: 20));

        quote.Type.Should().Be(RefundType.Prorated);
        quote.Amount.Should().Be(1035m); // 1095 * 345 / 365
    }

    [Fact]
    public void Amount_is_capped_at_what_is_still_refundable()
    {
        var quote = RefundPolicy.Quote(Input(periodPaid: 1150m, refundable: 400m, serviceDays: 5));

        quote.Amount.Should().Be(400m);
    }

    [Fact]
    public void Already_fully_refunded_is_rejected()
    {
        var act = () => RefundPolicy.Quote(Input(periodPaid: 1150m, refundable: 0m, serviceDays: 5));

        act.Should().Throw<ConflictException>().WithMessage("*Nothing is refundable*");
    }

    [Fact]
    public void Outside_the_prorated_window_is_rejected()
    {
        var act = () => RefundPolicy.Quote(Input(periodPaid: 1150m, refundable: 1150m, serviceDays: 46));

        act.Should().Throw<ConflictException>().WithMessage("*window*");
    }

    [Fact]
    public void Zero_received_is_rejected()
    {
        var act = () => RefundPolicy.Quote(Input(periodPaid: 0m, refundable: 100m, serviceDays: 3));

        act.Should().Throw<ConflictException>();
    }
}
