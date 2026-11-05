using Shared.Contracts.Localization;
using Shared.Contracts.Pagination;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetAdminPlans;

public class GetAdminPlansHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetAdminPlansQuery, PaginatedResult<AdminPlanDto>>
{
    public async Task<PaginatedResult<AdminPlanDto>> Handle(
        GetAdminPlansQuery query,
        CancellationToken cancellationToken)
    {
        var catalog = await dbContext.FeatureDefinitions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Key)
            .Select(x => new { x.Key, x.DisplayName.En, x.DisplayName.Ar })
            .ToListAsync(cancellationToken);

        var queryable = dbContext.Plans
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                x.Price,
                x.Currency,
                x.BillingCycleInMonths,
                x.MaxItems,
                x.MaxStorageGb,
                x.MaxUsers,
                x.IsActive,
                x.IsPublic,
                x.Version,
                x.UpdatedAt,
                x.DiscountPercent,
                x.PendingPrice,
                x.PriceEffectiveAt,
                x.PriceChangeAnnouncedAt,
                x.TrialDays,
                SubscriberCount = dbContext.Subscriptions.Count(s =>
                    s.PlanId == x.Id &&
                    (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue || s.Status == SubscriptionStatus.Trialing)),
                FeatureKeys = x.Features.Select(f => f.Key).ToList()
            });

        var paged = await queryable.PagedResult(query.PageNumber, query.PageSize, cancellationToken);

        var items = (paged.Result ?? []).Select(p => new AdminPlanDto(
            p.Id,
            LocalizedText.Create(p.Name.En, p.Name.Ar),
            p.Description != null ? LocalizedText.Create(p.Description.En, p.Description.Ar) : null,
            p.Price,
            p.Currency,
            p.BillingCycleInMonths,
            p.MaxItems,
            p.MaxStorageGb,
            p.MaxUsers,
            p.IsActive,
            p.IsPublic,
            p.Version,
            p.SubscriberCount,
            p.UpdatedAt,
            catalog.Select(f => new AdminPlanFeatureDto(
                f.Key,
                LocalizedText.Create(f.En, f.Ar),
                p.FeatureKeys.Contains(f.Key)))
                .ToList(),
            p.DiscountPercent,
            p.PendingPrice,
            p.PriceEffectiveAt,
            p.PriceChangeAnnouncedAt,
            p.TrialDays))
            .ToList();

        return new PaginatedResult<AdminPlanDto>
        {
            PageInfoResult = paged.PageInfoResult,
            Result = items
        };
    }
}
