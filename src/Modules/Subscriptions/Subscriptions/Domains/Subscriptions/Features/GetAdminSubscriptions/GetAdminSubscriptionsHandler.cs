using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.GetAdminSubscriptions;

public class GetAdminSubscriptionsQueryValidator : AbstractValidator<GetAdminSubscriptionsQuery>
{
    public GetAdminSubscriptionsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Currency).MaximumLength(3);
        RuleFor(x => x)
            .Must(x => x.MinAmount is null || x.MaxAmount is null || x.MinAmount <= x.MaxAmount)
            .WithMessage("MinAmount must not exceed MaxAmount.");
        RuleFor(x => x)
            .Must(x => x.RenewalFrom is null || x.RenewalTo is null || x.RenewalFrom < x.RenewalTo)
            .WithMessage("RenewalFrom must be before RenewalTo.");
    }
}

/// <summary>
/// Admin subscription search. Every filter, the text search (ILIKE on the denormalized subscriber name/email and the
/// plan name), the sort and the paging run in SQL.
/// </summary>
public class GetAdminSubscriptionsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetAdminSubscriptionsQuery, PaginatedResult<AdminSubscriptionListItemDto>>
{
    public async Task<PaginatedResult<AdminSubscriptionListItemDto>> Handle(
        GetAdminSubscriptionsQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = Filter(dbContext.Subscriptions.AsNoTracking(), query);

        queryable = queryable.OrderByDescending(x => x.UpdatedAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var result = await queryable
            .Select(AdminBillingProjections.ToAdminSubscriptionListItemDto)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
        result.OrderBy = query.SortColumn;
        result.OrderByDesc = query.SortBy != Shared.Contracts.Enums.SortBy.Ascending;
        return result;
    }

    /// <summary>The SQL filter (public so tests can assert the generated SQL).</summary>
    public static IQueryable<Subscription> Filter(IQueryable<Subscription> queryable, GetAdminSubscriptionsQuery query)
    {
        if (query.Status.HasValue)
            queryable = queryable.Where(x => x.Status == query.Status.Value);

        if (query.PlanId.HasValue)
            queryable = queryable.Where(x => x.PlanId == query.PlanId.Value);

        if (!string.IsNullOrWhiteSpace(query.Currency))
        {
            var currency = query.Currency.Trim().ToUpperInvariant();
            queryable = queryable.Where(x => x.Currency == currency);
        }

        // timestamptz parameters must be UTC (offset 0) - normalize client-supplied offsets.
        if (query.RenewalFrom.AsUtc() is { } renewalFrom)
            queryable = queryable.Where(x => x.RenewalDate >= renewalFrom);

        if (query.RenewalTo.AsUtc() is { } renewalTo)
            queryable = queryable.Where(x => x.RenewalDate < renewalTo);

        if (query.MinAmount.HasValue)
            queryable = queryable.Where(x => x.Price >= query.MinAmount.Value);

        if (query.MaxAmount.HasValue)
            queryable = queryable.Where(x => x.Price <= query.MaxAmount.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            if (Guid.TryParse(query.Search.Trim(), out var id))
            {
                queryable = queryable.Where(x => x.Id == id || x.SubscriberId == id);
            }
            else
            {
                var pattern = AdminBillingProjections.ContainsPattern(query.Search);
                queryable = queryable.Where(x =>
                    EF.Functions.ILike(x.SubscriberDisplayName ?? string.Empty, pattern, AdminBillingProjections.LikeEscape) ||
                    EF.Functions.ILike(x.SubscriberEmail ?? string.Empty, pattern, AdminBillingProjections.LikeEscape) ||
                    EF.Functions.ILike(x.PlanName, pattern, AdminBillingProjections.LikeEscape));
            }
        }

        return queryable;
    }
}
