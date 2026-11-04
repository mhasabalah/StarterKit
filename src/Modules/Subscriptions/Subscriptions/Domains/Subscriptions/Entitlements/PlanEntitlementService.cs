using Subscriptions.Contracts.Subscribers;

using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Entitlements;

/// <summary>
/// <see cref="IEntitlementService"/> answered from the subscriber's Active/PastDue subscription (plan features and
/// limits are snapshotted on it). With <c>Subscriptions:Entitlements:Enforce = false</c> (default) every feature is
/// allowed and no limit applies; the decision is logged at debug level so a rollout can be observed first.
/// </summary>
public sealed class PlanEntitlementService(
    SubscriptionsDbContext dbContext,
    ISubscriberIdProvider subscriberIdProvider,
    IEntitlementCache cache,
    IOptions<SubscriptionOptions> options,
    ILogger<PlanEntitlementService> logger) : IEntitlementService
{
    private EntitlementSnapshot? _snapshot;

    public async Task<bool> HasFeatureAsync(string key, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var has = snapshot.Features.TryGetValue(key, out var value) && bool.TryParse(value, out var on) && on;

        if (options.Value.Entitlements.Enforce)
            return has;

        if (!has)
            logger.LogDebug("Entitlements not enforced: feature {Feature} is not in the plan but is allowed", key);
        return true;
    }

    public async Task<int?> GetLimitAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Entitlements.Enforce)
        {
            logger.LogDebug("Entitlements not enforced: limit {Limit} reported as unlimited", key);
            return null;
        }

        var snapshot = await GetSnapshotAsync(cancellationToken);
        return key switch
        {
            EntitlementKeys.MaxItems => snapshot.MaxItems,
            EntitlementKeys.MaxStorageGb => snapshot.MaxStorageGb,
            EntitlementKeys.MaxUsers => snapshot.MaxUsers,
            _ => snapshot.Features.TryGetValue(key, out var value) && int.TryParse(value, out var number) ? number : null
        };
    }

    private async Task<EntitlementSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is not null)
            return _snapshot;

        var subscriberId = subscriberIdProvider.GetCurrentSubscriberId();
        if (subscriberId is null)
            return _snapshot = EntitlementSnapshot.None;

        var cached = await cache.GetAsync(subscriberId.Value, cancellationToken);
        if (cached is not null)
            return _snapshot = cached;

        var row = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.SubscriberId == subscriberId.Value &&
                        (x.Status == SubscriptionStatus.Active || x.Status == SubscriptionStatus.PastDue))
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new { x.FeaturesJson, x.MaxItems, x.MaxStorageGb, x.MaxUsers })
            .FirstOrDefaultAsync(cancellationToken);

        var snapshot = row is null
            ? EntitlementSnapshot.None
            : new EntitlementSnapshot(row.MaxItems, row.MaxStorageGb, row.MaxUsers, FeaturesJson.Deserialize(row.FeaturesJson));

        await cache.SetAsync(subscriberId.Value, snapshot, cancellationToken);
        return _snapshot = snapshot;
    }
}
