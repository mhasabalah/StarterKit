using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.GetActivePlanLimits;

public class GetActivePlanLimitsHandler(
    SubscriptionsDbContext dbContext,
    IDistributedCache cache)
    : IQueryHandler<GetActivePlanLimitsQuery, ActivePlanLimitsDto>
{
    private static readonly DistributedCacheEntryOptions _shortTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };

    private static readonly DistributedCacheEntryOptions _normalTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    public async Task<ActivePlanLimitsDto> Handle(
        GetActivePlanLimitsQuery query,
        CancellationToken cancellationToken)
    {
        var cacheKey = SubscriptionCacheKeys.ForSubscriber(query.SubscriberId);
        var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
        if (cached is not null)
        {
            var cachedPayload = SubscriptionCacheSerializer.DeserializePayload(cached);
            if (cachedPayload is not null)
                return cachedPayload.ToLimitsDto();
        }

        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.SubscriberId == query.SubscriberId &&
                        (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue))
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new
            {
                x.MaxItems,
                x.MaxStorageGb,
                x.MaxUsers,
                Status = x.Status.ToString(),
                x.GracePeriodEndsAt,
                x.FeaturesJson
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is null)
        {
            var noSubscription = new ActivePlanLimitsDto(null, null, null, "NoSubscription", null);
            await cache.SetStringAsync(
                cacheKey,
                SubscriptionCacheSerializer.SerializePayload(
                    SubscriptionCacheSerializer.CreatePayload(noSubscription, [])),
                _shortTtl,
                cancellationToken);
            return noSubscription;
        }

        var result = new ActivePlanLimitsDto(
            subscription.MaxItems,
            subscription.MaxStorageGb,
            subscription.MaxUsers,
            subscription.Status,
            subscription.GracePeriodEndsAt);
        var features = SubscriptionCacheSerializer.ExtractFeatures(subscription.FeaturesJson);

        await cache.SetStringAsync(
            cacheKey,
            SubscriptionCacheSerializer.SerializePayload(
                SubscriptionCacheSerializer.CreatePayload(result, features)),
            _normalTtl,
            cancellationToken);
        return result;
    }
}
