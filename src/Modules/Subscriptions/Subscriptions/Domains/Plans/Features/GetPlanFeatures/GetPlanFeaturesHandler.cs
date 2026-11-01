using Shared.Contracts.Localization;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetPlanFeatures;

public class GetPlanFeaturesHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPlanFeaturesQuery, List<AdminPlanFeatureDto>>
{
    public async Task<List<AdminPlanFeatureDto>> Handle(
        GetPlanFeaturesQuery query,
        CancellationToken cancellationToken)
    {
        var catalog = await dbContext.FeatureDefinitions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Key)
            .Select(x => new { x.Key, x.DisplayName.En, x.DisplayName.Ar })
            .ToListAsync(cancellationToken);

        var assignedKeys = await dbContext.Plans
            .AsNoTracking()
            .Where(x => x.Id == query.PlanId)
            .SelectMany(x => x.Features.Select(f => f.Key))
            .ToListAsync(cancellationToken);

        var assignedSet = assignedKeys.ToHashSet();

        return catalog
            .Select(f => new AdminPlanFeatureDto(
                f.Key,
                LocalizedText.Create(f.En, f.Ar),
                assignedSet.Contains(f.Key)))
            .ToList();
    }
}
