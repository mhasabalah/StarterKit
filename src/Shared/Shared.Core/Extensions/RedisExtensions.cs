using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Shared.Core.HealthCheck;
using Shared.Core.Options;
using StackExchange.Redis;

namespace Shared.Core.Extensions;

/// <summary>
/// Extension methods for configuring the Redis distributed cache.
/// </summary>
public static class RedisExtensions
{
    /// <summary>Health check name (tag <see cref="HealthCheckTags.Ready"/>).</summary>
    public const string HealthCheckName = "redis";

    /// <summary>
    /// Registers <see cref="IDistributedCache"/>. With <c>Redis:Enabled = true</c> (default) that is Redis over ONE
    /// process-wide <see cref="IConnectionMultiplexer"/> (also used by the readiness health check and any other
    /// Redis consumer); with <c>Redis:Enabled = false</c> it is the in-process memory cache.
    /// Connection resolution: <see cref="RedisConnectionConfiguration"/>.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <param name="instanceNameSuffix">Optional suffix for the key prefix (e.g. "TokenBlacklist")</param>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration,
        string? instanceNameSuffix = null)
    {
        var section = configuration.GetSection(RedisOptions.SectionName);
        services.Configure<RedisOptions>(section);
        var redisOptions = section.Get<RedisOptions>() ?? new RedisOptions();

        if (!redisOptions.Enabled)
        {
            services.AddDistributedMemoryCache();
            return services;
        }

        // Resolved eagerly so a misconfiguration (e.g. missing production secret) fails at startup, not on first use.
        var redisConfiguration = RedisConnectionConfiguration.Resolve(configuration, redisOptions);

        // One multiplexer per process (StackExchange.Redis guidance). With AbortOnConnectFail = false Connect returns
        // even while Redis is down and keeps reconnecting in the background.
        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConfiguration));

        var instanceName = string.IsNullOrEmpty(instanceNameSuffix)
            ? redisOptions.InstanceName
            : redisOptions.InstanceName.TrimEnd(':') + $":{instanceNameSuffix}:";

        services.AddStackExchangeRedisCache(options => options.InstanceName = instanceName);
        services.AddOptions<RedisCacheOptions>()
            .Configure<IServiceProvider>((options, sp) =>
                options.ConnectionMultiplexerFactory =
                    () => Task.FromResult(sp.GetRequiredService<IConnectionMultiplexer>()));

        services.AddHealthChecks()
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: HealthCheckName,
                tags: [HealthCheckTags.Ready]);

        return services;
    }

    /// <summary>
    /// Get a JSON value from the cache. Cache failures are logged and treated as a miss (null);
    /// cancellation of <paramref name="cancellationToken"/> is NOT swallowed.
    /// </summary>
    public static async Task<T?> GetFromCacheAsync<T>(
        this IDistributedCache cache,
        string key,
        ILogger? logger = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var data = await cache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrEmpty(data))
            {
                return null;
            }

            return System.Text.Json.JsonSerializer.Deserialize<T>(data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to get key {Key} from cache", key);
            return null;
        }
    }

    /// <summary>
    /// Set value in cache with proper error handling
    /// </summary>
    public static async Task SetInCacheAsync<T>(
        this IDistributedCache cache,
        string key,
        T value,
        TimeSpan? expiration = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var data = System.Text.Json.JsonSerializer.Serialize(value);
            var options = new DistributedCacheEntryOptions();

            if (expiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiration;
            }

            await cache.SetStringAsync(key, data, options, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to set key {Key} in cache", key);
        }
    }

    /// <summary>
    /// Remove value from cache with proper error handling
    /// </summary>
    public static async Task RemoveFromCacheAsync(
        this IDistributedCache cache,
        string key,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to remove key {Key} from cache", key);
        }
    }
}
