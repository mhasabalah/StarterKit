using Subscriptions.Contracts.Ledger.DTOs;

namespace Subscriptions.Domains.Ledger;

/// <summary>Sum of the active prices of one (currency, billing cycle) group (aggregated in SQL).</summary>
public sealed record ActivePriceGroup(string Currency, int BillingCycleInMonths, decimal PriceSum, int Count);

/// <summary>Sum of ledger amounts of one (currency, type, plan, method) group (aggregated in SQL).</summary>
public sealed record LedgerAmountGroup(
    string Currency,
    LedgerEntryType Type,
    Guid? PlanId,
    string? PlanName,
    string? PaymentMethod,
    decimal Amount);

/// <summary>
/// The pure KPI math of the billing dashboard (unit-tested). Inputs are SQL aggregates; money is never mixed across
/// currencies; rounding (2 decimals, away from zero) happens once, at the end.
/// <list type="bullet">
/// <item>MRR = Σ price / cycle months over Active, non-complimentary subscriptions; ARR = MRR × 12.</item>
/// <item>Revenue: Gross = Σ Payment; Refunds = −Σ Refund; CreditNotes = −Σ CreditNote; Adjustments = Σ Adjustment;
/// Net = Gross − Refunds − CreditNotes + Adjustments (= the signed sum).</item>
/// </list>
/// </summary>
public static class BillingDashboardCalculator
{
    public const string UnspecifiedMethod = "Unspecified";

    public static IReadOnlyList<RecurringRevenueDto> RecurringRevenue(IEnumerable<ActivePriceGroup> groups) =>
        groups
            .Where(g => g.BillingCycleInMonths > 0)
            .GroupBy(g => g.Currency.ToUpperInvariant())
            .Select(c =>
            {
                var mrr = c.Sum(g => g.PriceSum / g.BillingCycleInMonths);
                return new RecurringRevenueDto(c.Key, Round(mrr), Round(mrr * 12m), c.Sum(g => g.Count));
            })
            .OrderBy(x => x.Currency, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<RevenueSummaryDto> Revenue(IEnumerable<LedgerAmountGroup> rows) =>
        rows
            .GroupBy(r => r.Currency.ToUpperInvariant())
            .Select(c =>
            {
                var t = Totals(c);
                return new RevenueSummaryDto(c.Key, t.Gross, t.Refunds, t.CreditNotes, t.Adjustments, t.Net);
            })
            .OrderBy(x => x.Currency, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<RevenueBreakdownDto> RevenueByPlan(IEnumerable<LedgerAmountGroup> rows) =>
        Breakdown(rows, r => r.PlanId?.ToString() ?? string.Empty, r => r.PlanName ?? "Unknown plan");

    public static IReadOnlyList<RevenueBreakdownDto> RevenueByPaymentMethod(IEnumerable<LedgerAmountGroup> rows) =>
        Breakdown(
            rows,
            r => string.IsNullOrWhiteSpace(r.PaymentMethod) ? UnspecifiedMethod : r.PaymentMethod,
            r => string.IsNullOrWhiteSpace(r.PaymentMethod) ? UnspecifiedMethod : r.PaymentMethod);

    /// <summary>Default dashboard range: the current calendar month in UTC, [first day, first day of next month).</summary>
    public static (DateTimeOffset From, DateTimeOffset To) ResolveRange(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)
    {
        var utcNow = now.ToUniversalTime();
        var monthStart = new DateTimeOffset(utcNow.Year, utcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var start = (from ?? monthStart).ToUniversalTime();
        var end = (to ?? (from is null ? monthStart.AddMonths(1) : start.AddMonths(1))).ToUniversalTime();
        if (end <= start)
            throw new BadRequestException("'to' must be after 'from'.");
        if (end - start > TimeSpan.FromDays(3700))
            throw new BadRequestException("The range must not exceed 10 years.");
        return (start, end);
    }

    public static double? AgeHours(DateTimeOffset? oldest, DateTimeOffset now) =>
        oldest is { } at ? Math.Round(Math.Max(0d, (now - at).TotalHours), 1) : null;

    private static IReadOnlyList<RevenueBreakdownDto> Breakdown(
        IEnumerable<LedgerAmountGroup> rows,
        Func<LedgerAmountGroup, string> key,
        Func<LedgerAmountGroup, string> label) =>
        rows
            .GroupBy(r => (Currency: r.Currency.ToUpperInvariant(), Key: key(r)))
            .Select(g =>
            {
                var t = Totals(g);
                var name = g.Select(label).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? g.Key.Key;
                return new RevenueBreakdownDto(g.Key.Currency, g.Key.Key, name, t.Gross, t.Refunds, t.CreditNotes, t.Adjustments, t.Net);
            })
            .OrderBy(x => x.Currency, StringComparer.Ordinal)
            .ThenByDescending(x => x.Gross)
            .ThenBy(x => x.Label, StringComparer.Ordinal)
            .ToList();

    private static (decimal Gross, decimal Refunds, decimal CreditNotes, decimal Adjustments, decimal Net) Totals(
        IEnumerable<LedgerAmountGroup> rows)
    {
        decimal gross = 0m, refunds = 0m, credits = 0m, adjustments = 0m;
        foreach (var row in rows)
        {
            switch (row.Type)
            {
                case LedgerEntryType.Payment:
                    gross += row.Amount;
                    break;
                case LedgerEntryType.Refund:
                    refunds -= row.Amount;
                    break;
                case LedgerEntryType.CreditNote:
                    credits -= row.Amount;
                    break;
                case LedgerEntryType.Adjustment:
                    adjustments += row.Amount;
                    break;
            }
        }

        gross = Round(gross);
        refunds = Round(refunds);
        credits = Round(credits);
        adjustments = Round(adjustments);
        return (gross, refunds, credits, adjustments, gross - refunds - credits + adjustments);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
