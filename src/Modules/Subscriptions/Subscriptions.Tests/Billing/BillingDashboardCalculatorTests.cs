using FluentAssertions;
using Shared.Core.Exceptions;
using Subscriptions.Contracts.Ledger.Enums;
using Subscriptions.Domains.Ledger;
using Xunit;

namespace Subscriptions.Tests.Billing;

public class BillingDashboardCalculatorTests
{
    private static readonly Guid Basic = Guid.NewGuid();
    private static readonly Guid Pro = Guid.NewGuid();

    [Fact]
    public void Mrr_mixes_monthly_and_annual_cycles_per_month()
    {
        var mrr = BillingDashboardCalculator.RecurringRevenue(
        [
            new ActivePriceGroup("SAR", 1, 300m, 3),   // 3 monthly subscriptions at 100
            new ActivePriceGroup("SAR", 12, 2400m, 2), // 2 annual subscriptions at 1200 = 200 / month
            new ActivePriceGroup("SAR", 3, 90m, 1)     // 1 quarterly at 90 = 30 / month
        ]).Single();

        mrr.Currency.Should().Be("SAR");
        mrr.Mrr.Should().Be(530m);
        mrr.Arr.Should().Be(6360m);
        mrr.ActiveSubscriptions.Should().Be(6);
    }

    [Fact]
    public void Mrr_rounds_once_at_the_end()
    {
        // 3 annual subscriptions at 100: 25.00 exactly (rounding each 8.333.. would give 24.99).
        var mrr = BillingDashboardCalculator.RecurringRevenue([new ActivePriceGroup("USD", 12, 300m, 3)]).Single();

        mrr.Mrr.Should().Be(25m);
        mrr.Arr.Should().Be(300m);
    }

    [Fact]
    public void Currencies_are_never_mixed()
    {
        var mrr = BillingDashboardCalculator.RecurringRevenue(
        [
            new ActivePriceGroup("SAR", 1, 100m, 1),
            new ActivePriceGroup("USD", 1, 50m, 1),
            new ActivePriceGroup("sar", 12, 1200m, 1)
        ]);

        mrr.Should().HaveCount(2);
        mrr.Single(x => x.Currency == "SAR").Mrr.Should().Be(200m);
        mrr.Single(x => x.Currency == "USD").Mrr.Should().Be(50m);

        var revenue = BillingDashboardCalculator.Revenue(
        [
            Row("SAR", LedgerEntryType.Payment, 1000m),
            Row("USD", LedgerEntryType.Payment, 70m),
            Row("USD", LedgerEntryType.Refund, -20m)
        ]);

        revenue.Single(x => x.Currency == "SAR").Net.Should().Be(1000m);
        revenue.Single(x => x.Currency == "USD").Net.Should().Be(50m);
    }

    [Fact]
    public void Net_is_gross_minus_refunds()
    {
        var revenue = BillingDashboardCalculator.Revenue(
        [
            Row("SAR", LedgerEntryType.Payment, 1150m),
            Row("SAR", LedgerEntryType.Payment, 575m),
            Row("SAR", LedgerEntryType.Refund, -230m)
        ]).Single();

        revenue.Gross.Should().Be(1725m);
        revenue.Refunds.Should().Be(230m, "refunds are reported as a positive amount");
        revenue.Net.Should().Be(1495m);
        revenue.Net.Should().Be(revenue.Gross - revenue.Refunds);
    }

    [Fact]
    public void Credit_notes_and_adjustments_are_separate_buckets_and_net_is_the_signed_sum()
    {
        var rows = new[]
        {
            Row("SAR", LedgerEntryType.Payment, 1000m),
            Row("SAR", LedgerEntryType.Refund, -100m),
            Row("SAR", LedgerEntryType.CreditNote, -50m),
            Row("SAR", LedgerEntryType.Adjustment, 0m),
            Row("SAR", LedgerEntryType.Adjustment, 10m)
        };

        var revenue = BillingDashboardCalculator.Revenue(rows).Single();

        revenue.Should().BeEquivalentTo(new { Gross = 1000m, Refunds = 100m, CreditNotes = 50m, Adjustments = 10m, Net = 860m });
        revenue.Net.Should().Be(rows.Sum(x => x.Amount));
    }

    [Fact]
    public void Revenue_is_grouped_by_plan_and_by_payment_method_per_currency()
    {
        var rows = new[]
        {
            Row("SAR", LedgerEntryType.Payment, 300m, Basic, "Basic", "BankTransfer"),
            Row("SAR", LedgerEntryType.Payment, 200m, Basic, "Basic", "Cash"),
            Row("SAR", LedgerEntryType.Refund, -50m, Basic, "Basic", null),
            Row("SAR", LedgerEntryType.Payment, 900m, Pro, "Pro", "BankTransfer"),
            Row("USD", LedgerEntryType.Payment, 40m, Pro, "Pro", "BankTransfer")
        };

        var byPlan = BillingDashboardCalculator.RevenueByPlan(rows);
        byPlan.Should().HaveCount(3);
        byPlan.Single(x => x.Currency == "SAR" && x.Key == Basic.ToString())
            .Should().BeEquivalentTo(new { Label = "Basic", Gross = 500m, Refunds = 50m, Net = 450m });
        byPlan.Single(x => x.Currency == "SAR" && x.Key == Pro.ToString()).Net.Should().Be(900m);
        byPlan.Single(x => x.Currency == "USD").Net.Should().Be(40m);
        byPlan.First().Label.Should().Be("Pro", "within a currency the biggest gross comes first");

        var byMethod = BillingDashboardCalculator.RevenueByPaymentMethod(rows);
        byMethod.Single(x => x.Currency == "SAR" && x.Key == "BankTransfer").Gross.Should().Be(1200m);
        byMethod.Single(x => x.Currency == "SAR" && x.Key == "Cash").Gross.Should().Be(200m);
        byMethod.Single(x => x.Currency == "SAR" && x.Key == BillingDashboardCalculator.UnspecifiedMethod).Refunds.Should().Be(50m);
    }

    [Fact]
    public void Default_range_is_the_current_utc_month()
    {
        var now = new DateTimeOffset(2026, 11, 15, 22, 30, 0, TimeSpan.FromHours(3));

        var (from, to) = BillingDashboardCalculator.ResolveRange(null, null, now);

        from.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        to.Should().Be(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Explicit_range_is_kept_and_a_missing_end_defaults_to_one_month_after_from()
    {
        var from = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);

        BillingDashboardCalculator.ResolveRange(from, from.AddDays(7), DateTimeOffset.UtcNow)
            .Should().Be((from, from.AddDays(7)));
        BillingDashboardCalculator.ResolveRange(from, null, DateTimeOffset.UtcNow).To.Should().Be(from.AddMonths(1));
    }

    [Fact]
    public void An_inverted_range_is_a_bad_request()
    {
        var at = DateTimeOffset.UtcNow;
        var act = () => BillingDashboardCalculator.ResolveRange(at, at.AddDays(-1), at);

        act.Should().Throw<BadRequestException>();
    }

    [Fact]
    public void Queue_age_is_in_hours_with_one_decimal()
    {
        var now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

        BillingDashboardCalculator.AgeHours(now.AddHours(-30).AddMinutes(-6), now).Should().Be(30.1);
        BillingDashboardCalculator.AgeHours(null, now).Should().BeNull();
        BillingDashboardCalculator.AgeHours(now.AddMinutes(5), now).Should().Be(0, "clock skew never yields a negative age");
    }

    private static LedgerAmountGroup Row(
        string currency,
        LedgerEntryType type,
        decimal amount,
        Guid? planId = null,
        string? planName = null,
        string? method = null) =>
        new(currency, type, planId, planName, method, amount);
}
