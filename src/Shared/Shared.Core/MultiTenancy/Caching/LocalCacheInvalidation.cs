using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Shared.Core.MultiTenancy.Caching;

/// <summary>
/// Cross-instance invalidation of in-process (L1, <see cref="IMemoryCache"/>) entries. The instance that changes data
/// evicts its own entry and calls <see cref="PublishAsync"/>; every other instance evicts the same keys on receipt
/// (idempotent), so a suspended tenant stops being served everywhere at once instead of after
/// <c>MultiTenancy:Cache:LocalSeconds</c>. Best effort: a broadcast failure is logged and never fails the caller
/// (the L1 TTL remains the upper bound).
/// </summary>
public interface ILocalCacheInvalidationBus
{
    Task PublishAsync(IReadOnlyCollection<string> memoryCacheKeys, CancellationToken cancellationToken = default);
}

/// <summary>Used without Redis: single instance, so the local eviction already done by the caller is enough.</summary>
public sealed class NoOpLocalCacheInvalidationBus : ILocalCacheInvalidationBus
{
    public Task PublishAsync(IReadOnlyCollection<string> memoryCacheKeys, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>
/// Redis pub/sub implementation (<c>MultiTenancy:Cache:InvalidationChannel</c>, default
/// <c>starterkit:tenancy:invalidate</c>) and the subscriber that evicts received keys from the local
/// <see cref="IMemoryCache"/>. Registered as a hosted service only when a Redis multiplexer exists.
/// </summary>
public sealed class RedisLocalCacheInvalidationBus(
    IConnectionMultiplexer redis,
    IMemoryCache memoryCache,
    IOptions<MultiTenancyOptions> options,
    ILogger<RedisLocalCacheInvalidationBus> logger) : ILocalCacheInvalidationBus, IHostedService
{
    private RedisChannel Channel => RedisChannel.Literal(options.Value.Cache.InvalidationChannel);

    public async Task PublishAsync(IReadOnlyCollection<string> memoryCacheKeys, CancellationToken cancellationToken = default)
    {
        if (memoryCacheKeys.Count == 0 || !options.Value.Cache.BroadcastInvalidation) return;

        try
        {
            await redis.GetSubscriber().PublishAsync(Channel, JsonSerializer.Serialize(memoryCacheKeys));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Broadcasting cache invalidation of {Count} key(s) failed; other instances expire by TTL",
                memoryCacheKeys.Count);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Cache.BroadcastInvalidation) return;

        try
        {
            await redis.GetSubscriber().SubscribeAsync(Channel, (_, message) => Evict(message.ToString()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Subscribing to cache invalidations failed; this instance relies on the L1 TTL");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetSubscriber().UnsubscribeAsync(Channel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Unsubscribing from cache invalidations failed");
        }
    }

    /// <summary>Evicts the keys of one received message. Malformed messages are ignored (logged).</summary>
    public void Evict(string payload)
    {
        try
        {
            foreach (var key in JsonSerializer.Deserialize<string[]>(payload) ?? [])
            {
                memoryCache.Remove(key);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Ignoring malformed cache invalidation message");
        }
    }
}
