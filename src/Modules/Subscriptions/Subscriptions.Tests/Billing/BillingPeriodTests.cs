using FluentAssertions;
using Subscriptions.Domains.Subscriptions.Billing;
using Xunit;

namespace Subscriptions.Tests.Billing;

public class BillingPeriodTests
{
    private static DateTimeOffset Utc(int y, int m, int d, int h = 10) => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Monthly_anchor_on_Jan_31_clamps_to_month_end_without_drifting()
    {
        var anchor = Utc(2025, 1, 31);

        var first = BillingPeriod.FirstRenewal(anchor, 1);
        var second = BillingPeriod.NextRenewal(anchor, first, 1);
        var third = BillingPeriod.NextRenewal(anchor, second, 1);
        var fourth = BillingPeriod.NextRenewal(anchor, third, 1);

        first.Should().Be(Utc(2025, 2, 28));
        second.Should().Be(Utc(2025, 3, 31)); // back to the 31st, not stuck on the 28th
        third.Should().Be(Utc(2025, 4, 30));
        fourth.Should().Be(Utc(2025, 5, 31));
    }

    [Fact]
    public void Leap_year_february_has_29_days()
    {
        BillingPeriod.FirstRenewal(Utc(2024, 1, 31), 1).Should().Be(Utc(2024, 2, 29));
        BillingPeriod.FirstRenewal(Utc(2023, 1, 31), 1).Should().Be(Utc(2023, 2, 28));
    }

    [Fact]
    public void Annual_cycle_from_leap_day_returns_to_leap_day_on_the_next_leap_year()
    {
        var anchor = Utc(2024, 2, 29);
        var renewal = BillingPeriod.FirstRenewal(anchor, 12);

        renewal.Should().Be(Utc(2025, 2, 28));
        renewal = BillingPeriod.NextRenewal(anchor, renewal, 12);
        renewal.Should().Be(Utc(2026, 2, 28));
        renewal = BillingPeriod.NextRenewal(anchor, renewal, 12);
        renewal = BillingPeriod.NextRenewal(anchor, renewal, 12);
        renewal.Should().Be(Utc(2028, 2, 29));
    }

    [Fact]
    public void Annual_cycle_advances_one_year()
    {
        var anchor = Utc(2026, 3, 15);

        BillingPeriod.NextRenewal(anchor, Utc(2027, 3, 15), 12).Should().Be(Utc(2028, 3, 15));
    }

    [Fact]
    public void Off_grid_legacy_renewal_advances_by_one_full_cycle()
    {
        var anchor = Utc(2026, 1, 10);

        BillingPeriod.NextRenewal(anchor, Utc(2026, 2, 13), 1).Should().Be(Utc(2026, 3, 13));
    }

    [Fact]
    public void Period_start_is_the_previous_grid_date()
    {
        var anchor = Utc(2025, 1, 31);

        BillingPeriod.PeriodStart(anchor, Utc(2025, 3, 31), 1).Should().Be(Utc(2025, 2, 28));
        BillingPeriod.PeriodStart(anchor, Utc(2025, 2, 28), 1).Should().Be(anchor);
    }

    [Fact]
    public void Remaining_fraction_uses_the_actual_period_length_in_decimal_days()
    {
        var start = Utc(2025, 2, 1, 0);
        var end = Utc(2025, 3, 1, 0); // 28 days, not 30

        BillingPeriod.Days(start, end).Should().Be(28m);
        BillingPeriod.RemainingFraction(start, end, Utc(2025, 2, 15, 0)).Should().Be(0.5m);
        BillingPeriod.RemainingFraction(start, end, Utc(2025, 2, 1, 12)).Should().Be(27.5m / 28m);
    }

    [Fact]
    public void Remaining_fraction_is_clamped()
    {
        var start = Utc(2025, 2, 1);
        var end = Utc(2025, 3, 1);

        BillingPeriod.RemainingFraction(start, end, Utc(2025, 1, 1)).Should().Be(1m);
        BillingPeriod.RemainingFraction(start, end, Utc(2025, 4, 1)).Should().Be(0m);
        BillingPeriod.RemainingFraction(end, start, Utc(2025, 2, 10)).Should().Be(0m);
    }

    [Fact]
    public void Non_positive_cycle_is_rejected()
    {
        var act = () => BillingPeriod.FirstRenewal(Utc(2025, 1, 1), 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
