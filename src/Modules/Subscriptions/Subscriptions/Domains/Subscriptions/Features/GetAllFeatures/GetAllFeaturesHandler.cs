using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.GetAllFeatures;

public class GetAllFeaturesHandler(
    SubscriptionsDbContext dbContext,
    IDistributedCache cache)
    : IQueryHandler<GetAllFeaturesQuery, Dictionary<string, string>>
{
    private static readonly DistributedCacheEntryOptions _shortTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };

    private static readonly DistributedCacheEntryOptions _normalTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    public async Task<Dictionary<string, string>> Handle(
        GetAllFeaturesQuery query,
        CancellationToken cancellationToken)
    {
        var cacheKey = SubscriptionCacheKeys.ForSubscriber(query.SubscriberId);
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            var cachedPayload = SubscriptionCacheSerializer.DeserializePayload(cached);
            if (cachedPayload is not null)
                return cachedPayload.Features;

            return SubscriptionCacheSerializer.ExtractFeatures(cached);
        }

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.SubscriberId == query.SubscriberId &&
                        (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue || x.Status == SubscriptionStatus.Trialing))
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new
            {
                x.FeaturesJson,
                x.MaxItems,
                x.MaxStorageGb,
                x.MaxUsers,
                Status = x.Status.ToString(),
                x.GracePeriodEndsAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            await cache.SetStringAsync(
                cacheKey,
                SubscriptionCacheSerializer.SerializePayload(
                    SubscriptionCacheSerializer.CreatePayload(
                        new ActivePlanLimitsDto(null, null, null, "NoSubscription", null),
                        [])),
                _shortTtl,
                cancellationToken);
            return [];
        }

        var features = SubscriptionCacheSerializer.ExtractFeatures(subscription.FeaturesJson);
        var payload = SubscriptionCacheSerializer.CreatePayload(
            new ActivePlanLimitsDto(
                subscription.MaxItems,
                subscription.MaxStorageGb,
                subscription.MaxUsers,
                subscription.Status,
                subscription.GracePeriodEndsAt),
            features);
        await cache.SetStringAsync(
            cacheKey,
            SubscriptionCacheSerializer.SerializePayload(payload),
            _normalTtl,
            cancellationToken);

        return features;
    }
}
