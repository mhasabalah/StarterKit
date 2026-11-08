using Subscriptions.Domains.Ledger.Services;

namespace Subscriptions.Domains.Ledger.Features.GetBillingDashboard;

public class GetBillingDashboardQueryValidator : AbstractValidator<GetBillingDashboardQuery>
{
    public GetBillingDashboardQueryValidator()
    {
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From < x.To)
            .WithMessage("From must be before To.");
    }
}

/// <summary>
/// Billing KPIs. Every figure is aggregated in SQL; <see cref="BillingDashboardCalculator"/> does the money math.
/// Revenue comes from the ledger only. The result is cached per range by <see cref="IBillingDashboardCache"/> for
/// <c>Subscriptions:BillingDashboardCacheMinutes</c> (default 5, 0 disables it), and every cached range is dropped
/// after a ledger entry, a payment review change or a subscription status change commits
/// (<c>BillingDashboardCacheInvalidationHandler</c>).
/// </summary>
public class GetBillingDashboardHandler(
    SubscriptionsDbContext dbContext,
    IBillingDashboardCache cache,
    TimeProvider timeProvider)
    : IQueryHandler<GetBillingDashboardQuery, BillingDashboardDto>
{
    public async Task<BillingDashboardDto> Handle(GetBillingDashboardQuery query, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var (from, to) = BillingDashboardCalculator.ResolveRange(query.From, query.To, now);

        if (await cache.GetAsync(from, to, cancellationToken) is { } hit)
        {
            return hit;
        }

        var dashboard = await ComputeAsync(from, to, now, cancellationToken);
        await cache.SetAsync(from, to, dashboard, cancellationToken);

        return dashboard;
    }

    private async Task<BillingDashboardDto> ComputeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var subscriptions = dbContext.Subscriptions.AsNoTracking();

        var activePrices = await subscriptions
            .Where(x => x.Status == SubscriptionStatus.Active &&
                        (x.ComplimentaryUntil == null || x.ComplimentaryUntil <= now))
            .GroupBy(x => new { x.Currency, x.BillingCycleInMonths })
            .Select(g => new ActivePriceGroup(g.Key.Currency, g.Key.BillingCycleInMonths, g.Sum(x => x.Price), g.Count()))
            .ToListAsync(ct);

        var statusCounts = await subscriptions
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var ledgerRows = await dbContext.LedgerEntries
            .AsNoTracking()
            .Where(x => x.OccurredAt >= from && x.OccurredAt < to)
            .GroupBy(x => new { x.Currency, x.Type, x.PlanId, x.PlanName, x.PaymentMethod })
            .Select(g => new LedgerAmountGroup(
                g.Key.Currency, g.Key.Type, g.Key.PlanId, g.Key.PlanName, g.Key.PaymentMethod, g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        var queue = await dbContext.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Submitted)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Oldest = g.Min(p => p.SubmittedAt ?? p.CreatedAt) })
            .FirstOrDefaultAsync(ct);

        var cancelled = await subscriptions.CountAsync(
            x => x.Status == SubscriptionStatus.Cancelled && x.EndDate >= from && x.EndDate < to, ct);
        var expired = await subscriptions.CountAsync(
            x => x.Status == SubscriptionStatus.Expired && x.EndDate >= from && x.EndDate < to, ct);
        var started = await subscriptions.CountAsync(x => x.StartDate >= from && x.StartDate < to, ct);

        var counts = statusCounts.ToDictionary(x => x.Status, x => x.Count);
        return new BillingDashboardDto(
            from,
            to,
            now,
            BillingDashboardCalculator.RecurringRevenue(activePrices),
            Enum.GetValues<SubscriptionStatus>()
                .Select(s => new StatusCountDto(s.ToString(), counts.GetValueOrDefault(s)))
                .ToList(),
            BillingDashboardCalculator.Revenue(ledgerRows),
            BillingDashboardCalculator.RevenueByPlan(ledgerRows),
            BillingDashboardCalculator.RevenueByPaymentMethod(ledgerRows),
            queue?.Count ?? 0,
            BillingDashboardCalculator.AgeHours(queue?.Oldest, now),
            counts.GetValueOrDefault(SubscriptionStatus.PastDue),
            cancelled,
            expired,
            cancelled + expired,
            started);
    }
}
