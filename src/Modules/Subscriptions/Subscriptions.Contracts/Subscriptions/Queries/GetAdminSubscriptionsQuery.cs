using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Contracts.Subscriptions.Queries;

/// <summary>
/// Admin subscription search (<c>GET admin/subscriptions</c>). All filters run in SQL.
/// <para><see cref="Search"/>: case-insensitive "contains" on the subscriber name/email snapshot and the plan name, or
/// an exact subscription/subscriber id when it parses as a GUID.</para>
/// <para>Amount range applies to the period price snapshot. Sort columns: any scalar of the subscription
/// (e.g. <c>RenewalDate</c>, <c>Price</c>, <c>SubscriberDisplayName</c>, <c>Status</c>, <c>UpdatedAt</c>).</para>
/// </summary>
public record GetAdminSubscriptionsQuery : BaseQuery, IQuery<PaginatedResult<AdminSubscriptionListItemDto>>
{
    public SubscriptionStatus? Status { get; init; }
    public Guid? PlanId { get; init; }
    public string? Search { get; init; }
    public DateTimeOffset? RenewalFrom { get; init; }
    public DateTimeOffset? RenewalTo { get; init; }
    public decimal? MinAmount { get; init; }
    public decimal? MaxAmount { get; init; }
    public string? Currency { get; init; }
}

/// <summary>Subscriber-360 of one subscription (<c>GET admin/subscriptions/{id}</c>).</summary>
public record GetAdminSubscriptionByIdQuery(Guid SubscriptionId) : IQuery<AdminSubscriptionDetailsDto>;
