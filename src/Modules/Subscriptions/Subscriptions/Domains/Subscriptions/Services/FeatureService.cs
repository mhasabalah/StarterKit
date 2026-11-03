using Subscriptions.Contracts.Subscribers;
using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Domains.Subscriptions.Services;

public class FeatureService(
    IDistributedCache cache,
    SubscriptionsDbContext dbContext,
    ISubscriberIdProvider subscriberIdProvider)
    : IFeatureService
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private Dictionary<string, string>? _features;
    private bool _isLoaded;

    private static readonly DistributedCacheEntryOptions _shortTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };

    private static readonly DistributedCacheEntryOptions _normalTtl =
        new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) };

    public async Task<bool> IsEnabledAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _features?.TryGetValue(key, out var value) == true &&
               bool.TryParse(value, out var enabled) &&
               enabled;
    }

    public async Task<int?> GetIntAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _features?.TryGetValue(key, out var value) == true &&
               int.TryParse(value, out var intValue)
            ? intValue
            : null;
    }

    public async Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _features?.TryGetValue(key, out var value) == true ? value : null;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
            return;

        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (_isLoaded)
                return;

            var subscriberId = subscriberIdProvider.GetCurrentSubscriberId();
            if (subscriberId is null)
            {
                _features = [];
                _isLoaded = true;
                return;
            }

            var cacheKey = SubscriptionCacheKeys.ForSubscriber(subscriberId.Value);
            var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                _features = SubscriptionCacheSerializer.ExtractFeatures(cached);
                _isLoaded = true;
                return;
            }

            var subscription = await dbContext.Subscriptions
                .AsNoTracking()
                .Where(x => x.SubscriberId == subscriberId.Value &&
                            (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue))
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new
                {
                    x.FeaturesJson,
                    x.MaxItems,
                    x.MaxStorageGb,
                    x.MaxUsers,
                    x.Status,
                    x.GracePeriodEndsAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (subscription is null)
            {
                var noSubscriptionPayload = SubscriptionCacheSerializer.SerializePayload(
                    SubscriptionCacheSerializer.CreatePayload(
                        new ActivePlanLimitsDto(null, null, null, "NoSubscription", null),
                        []));
                await cache.SetStringAsync(cacheKey, noSubscriptionPayload, _shortTtl, cancellationToken);
                _features = [];
                _isLoaded = true;
                return;
            }

            var features = FeaturesJson.Deserialize(subscription.FeaturesJson);
            var payload = SubscriptionCacheSerializer.SerializePayload(
                SubscriptionCacheSerializer.CreatePayload(
                    new ActivePlanLimitsDto(
                        subscription.MaxItems,
                        subscription.MaxStorageGb,
                        subscription.MaxUsers,
                        subscription.Status.ToString(),
                        subscription.GracePeriodEndsAt),
                    features));

            await cache.SetStringAsync(cacheKey, payload, _normalTtl, cancellationToken);
            _features = features;
            _isLoaded = true;
        }
        finally
        {
            _sync.Release();
        }
    }
}
