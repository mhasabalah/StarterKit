using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetPlans;

public class GetPlansQueryValidator : AbstractValidator<GetPlansQuery>
{
}

public class GetPlansHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPlansQuery, List<PlanDto>>
{
    public async Task<List<PlanDto>> Handle(GetPlansQuery query, CancellationToken cancellationToken)
    {
        _ = query;
        var isArabic = System.Threading.Thread.CurrentThread.CurrentUICulture.Name
            .StartsWith("ar", StringComparison.OrdinalIgnoreCase);

        var catalog = await dbContext.FeatureDefinitions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Key)
            .Select(x => new
            {
                x.Key,
                NameEn = x.DisplayName.En,
                NameAr = x.DisplayName.Ar
            })
            .ToListAsync(cancellationToken);

        var plans = await dbContext.Plans
            .AsNoTracking()
            .Where(x => x.IsActive && x.IsPublic && !x.IsDeleted)
            .OrderBy(x => x.Price)
            .Select(x => new
            {
                x.Id,
                NameEn = x.Name.En,
                NameAr = x.Name.Ar,
                DescriptionEn = x.Description != null ? x.Description.En : null,
                DescriptionAr = x.Description != null ? x.Description.Ar : null,
                x.Price,
                x.Currency,
                x.BillingCycleInMonths,
                x.MaxItems,
                x.MaxStorageGb,
                x.MaxUsers,
                x.DiscountPercent,
                x.PendingPrice,
                x.PriceEffectiveAt,
                FeatureKeys = x.Features.Select(f => f.Key).ToList()
            })
            .ToListAsync(cancellationToken);

        return plans.Select(p => new PlanDto(
            p.Id,
            isArabic ? p.NameAr : p.NameEn,
            isArabic ? p.DescriptionAr : p.DescriptionEn,
            p.Price,
            p.Currency,
            p.BillingCycleInMonths,
            p.MaxItems,
            p.MaxStorageGb,
            p.MaxUsers,
            catalog.Select(f => new PlanFeatureDto(
                f.Key,
                isArabic ? f.NameAr : f.NameEn,
                p.FeatureKeys.Contains(f.Key)))
                .ToList(),
            p.DiscountPercent,
            p.PendingPrice,
            p.PriceEffectiveAt))
            .ToList();
    }
}
