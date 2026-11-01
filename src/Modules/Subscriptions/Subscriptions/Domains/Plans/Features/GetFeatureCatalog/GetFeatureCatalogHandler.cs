using Shared.Contracts.Localization;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Plans.Features.GetFeatureCatalog;

public class GetFeatureCatalogHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetFeatureCatalogQuery, List<FeatureCatalogItemDto>>
{
    public async Task<List<FeatureCatalogItemDto>> Handle(
        GetFeatureCatalogQuery query,
        CancellationToken cancellationToken)
    {
        return await dbContext.FeatureDefinitions
            .AsNoTracking()
            .Where(x => query.IncludeInactive || x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Key)
            .Select(x => new FeatureCatalogItemDto(
                x.Key,
                LocalizedText.Create(x.DisplayName.En, x.DisplayName.Ar),
                x.Description != null
                    ? LocalizedText.Create(x.Description.En, x.Description.Ar)
                    : null,
                x.Category,
                x.SortOrder,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }
}
