using Shared.Contracts.Localization;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetPlanById;

public class GetPlanByIdHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPlanByIdQuery, AdminPlanDto?>
{
    public async Task<AdminPlanDto?> Handle(GetPlanByIdQuery query, CancellationToken cancellationToken)
    {
        var catalog = await dbContext.FeatureDefinitions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Key)
            .Select(x => new { x.Key, x.DisplayName.En, x.DisplayName.Ar })
            .ToListAsync(cancellationToken);

        return await dbContext.Plans
            .AsNoTracking()
            .Where(x => x.Id == query.PlanId)
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
                SubscriberCount = dbContext.Subscriptions.Count(s =>
                    s.PlanId == x.Id &&
                    (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.PastDue)),
                FeatureKeys = x.Features.Select(f => f.Key).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken) is { } p
            ? new AdminPlanDto(
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
                p.PriceChangeAnnouncedAt)
            : null;
    }
}
