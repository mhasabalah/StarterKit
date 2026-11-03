using Subscriptions.Contracts.Ledger.DTOs;

namespace Subscriptions.Contracts.Ledger.Queries;

/// <summary>
/// Admin billing KPIs (<c>GET admin/billing/dashboard</c>). Revenue range is [From, To) in UTC; default the current
/// calendar month. Cached per range for <c>Subscriptions:BillingDashboardCacheMinutes</c> (default 5).
/// </summary>
public record GetBillingDashboardQuery : IQuery<BillingDashboardDto>
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
}
