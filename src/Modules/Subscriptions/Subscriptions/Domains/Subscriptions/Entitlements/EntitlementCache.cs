using System.Text.Json;
using Shared.Core.MultiTenancy;

using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Entitlements;

/// <summary>What the active (Active or PastDue) subscription of one subscriber grants. No subscription = nothing.</summary>
public sealed record EntitlementSnapshot(
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    Dictionary<string, string> Features)
{
    public static EntitlementSnapshot None { get; } = new(null, null, null, []);
}

/// <summary>
/// Short-lived cache of <see cref="EntitlementSnapshot"/> per subscriber, keyed by the (globally unique) subscriber id
/// so it can be invalidated without an ambient tenant. Explicit invalidation from the subscription domain events
/// (<see cref="EntitlementCacheInvalidationHandler"/>) is the primary mechanism; the short TTL is the safety net.
/// Failures are swallowed with a warning: the cache must never fail a request.
/// </summary>
public interface IEntitlementCache
{
    Task<EntitlementSnapshot?> GetAsync(Guid subscriberId, CancellationToken cancellationToken = default);

    Task SetAsync(Guid subscriberId, EntitlementSnapshot snapshot, CancellationToken cancellationToken = default);

    Task InvalidateAsync(Guid subscriberId, CancellationToken cancellationToken = default);
}

public sealed class EntitlementCache(
    IDistributedCache cache,
    IOptions<SubscriptionOptions> options,
    ILogger<EntitlementCache> logger) : IEntitlementCache
{
    // Deliberately NOT tenant-prefixed: the subscriber id is globally unique (the tenant id in multi-tenant mode, the
    // user id otherwise), and invalidation also runs from anonymous webhooks and jobs that have no ambient tenant.
    private static string KeyFor(Guid subscriberId) => $"subscriptions:entitlements:{subscriberId:N}:v1";

    public async Task<EntitlementSnapshot?> GetAsync(Guid subscriberId, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await cache.GetStringAsync(KeyFor(subscriberId), cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<EntitlementSnapshot>(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading entitlements from cache failed; falling back to the database");
            return null;
        }
    }

    public async Task SetAsync(Guid subscriberId, EntitlementSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetStringAsync(KeyFor(subscriberId), JsonSerializer.Serialize(snapshot),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.Entitlements.CacheSeconds)
                }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not cache entitlements");
        }
    }

    public async Task InvalidateAsync(Guid subscriberId, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(KeyFor(subscriberId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not invalidate entitlements cache (it expires on its own)");
        }
    }
}
