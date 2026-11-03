namespace Subscriptions.Contracts.Ledger.DTOs;

/// <summary>
/// Billing KPIs. Money is never summed across currencies: every money figure is per currency.
/// Revenue figures come from the immutable ledger only.
/// </summary>
/// <param name="RecurringRevenue">MRR/ARR per currency over Active, non-complimentary subscriptions.</param>
/// <param name="Revenue">Gross / refunds / credit notes / adjustments / net per currency for [From, To).</param>
/// <param name="PendingProofs">Submitted proofs awaiting review (the queue).</param>
/// <param name="OldestPendingProofAgeHours">Age of the oldest proof in the queue, in hours (null when empty).</param>
/// <param name="ChurnedSubscriptions">Cancelled + expired with an end date in [From, To).</param>
/// <param name="NewSubscriptions">Activated (start date) in [From, To).</param>
public record BillingDashboardDto(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<RecurringRevenueDto> RecurringRevenue,
    IReadOnlyList<StatusCountDto> StatusCounts,
    IReadOnlyList<RevenueSummaryDto> Revenue,
    IReadOnlyList<RevenueBreakdownDto> RevenueByPlan,
    IReadOnlyList<RevenueBreakdownDto> RevenueByPaymentMethod,
    int PendingProofs,
    double? OldestPendingProofAgeHours,
    int PastDueCount,
    int CancelledInRange,
    int ExpiredInRange,
    int ChurnedSubscriptions,
    int NewSubscriptions);

/// <param name="Mrr">Monthly recurring revenue: sum of price / billing-cycle-months (price tax basis per config).</param>
/// <param name="Arr">Mrr × 12.</param>
public record RecurringRevenueDto(string Currency, decimal Mrr, decimal Arr, int ActiveSubscriptions);

public record StatusCountDto(string Status, int Count);

/// <param name="Gross">Money received (ledger Payment entries).</param>
/// <param name="Refunds">Money paid back (positive number).</param>
/// <param name="CreditNotes">Standalone credit notes (positive number).</param>
/// <param name="Adjustments">Signed sum of manual adjustments.</param>
/// <param name="Net">Gross − Refunds − CreditNotes + Adjustments (= sum of the signed entries).</param>
public record RevenueSummaryDto(
    string Currency,
    decimal Gross,
    decimal Refunds,
    decimal CreditNotes,
    decimal Adjustments,
    decimal Net);

/// <param name="Key">Plan id, or payment method name ("Unspecified" when the ledger row has none).</param>
/// <param name="Label">Plan name or payment method name.</param>
public record RevenueBreakdownDto(
    string Currency,
    string Key,
    string Label,
    decimal Gross,
    decimal Refunds,
    decimal CreditNotes,
    decimal Adjustments,
    decimal Net);
