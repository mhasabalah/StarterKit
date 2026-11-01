using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Core.Options;

namespace Shared.Core.Extensions;

/// <summary>
/// Extension methods for configuring Redis distributed cache
/// </summary>
public static class RedisExtensions
{
    /// <summary>
    /// Add Redis distributed cache with options pattern
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    /// <param name="instanceNameSuffix">Optional suffix for instance name (e.g., "TokenBlacklist")</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration,
        string? instanceNameSuffix = null)
    {
        // Configure options from appsettings
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));

        // Get options for immediate use
        var redisOptions = new RedisOptions();
        configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);

        // Get environment name
        var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production";

        if (environment != "Development")
        {
            OverrideFromEnvironment(redisOptions);
        }

        // Check if Redis is enabled
        if (!redisOptions.Enabled)
        {
            services.AddDistributedMemoryCache();
            return services;
        }

        // Validate configuration
        if (!redisOptions.IsValid())
        {
            throw new InvalidOperationException(
                "Invalid Redis configuration. Host and Port are required.");
        }

        // Build connection string
        var connectionString = redisOptions.GetConnectionString();

        // Build instance name
        var instanceName = redisOptions.InstanceName;
        if (!string.IsNullOrEmpty(instanceNameSuffix))
        {
            instanceName = instanceName.TrimEnd(':') + $":{instanceNameSuffix}:";
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = connectionString;
            options.InstanceName = instanceName;
        });

        return services;
    }

    /// <summary>
    /// Override Redis Host and Port from environment variables (Docker/Kubernetes)
    /// Only called in non-Development environments for Docker networking
    ///
    /// NOTE: Password is NOT overridden here - it's handled by GetPassword()
    /// in RedisOptions for security-aware logic (secrets vs env vars)
    /// </summary>
    private static void OverrideFromEnvironment(RedisOptions options)
    {
        var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST");
        if (!string.IsNullOrEmpty(redisHost))
        {
            options.Host = redisHost;
        }

        var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT");
        if (!string.IsNullOrEmpty(redisPort) && int.TryParse(redisPort, out var port))
        {
            options.Port = port;
        }
    }

    /// <summary>
    /// Health check extension for Redis
    /// Requires: Microsoft.Extensions.Diagnostics.HealthChecks.Redis package
    /// </summary>
    /// <example>
    /// // Add package: dotnet add package AspNetCore.HealthChecks.Redis
    /// // Then use: services.AddRedisHealthCheck(configuration);
    /// </example>
    // Commented out - Add AspNetCore.HealthChecks.Redis package to enable
    /*
    public static IServiceCollection AddRedisHealthCheck(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisOptions = new RedisOptions();
        configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);
        OverrideFromEnvironment(redisOptions);

        if (!redisOptions.Enabled)
        {
            return services;
        }

        var connectionString = redisOptions.GetConnectionString();

        services.AddHealthChecks()
            .AddRedis(connectionString, name: "redis", tags: new[] { "cache", "redis" });

        return services;
    }
    */

    /// <summary>
    /// Get IDistributedCache with proper error handling
    /// </summary>
    public static async Task<T?> GetFromCacheAsync<T>(
        this IDistributedCache cache,
        string key,
        ILogger? logger = null) where T : class
    {
        try
        {
            var data = await cache.GetStringAsync(key);
            if (string.IsNullOrEmpty(data))
            {
                return null;
            }

            return System.Text.Json.JsonSerializer.Deserialize<T>(data);
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
        ILogger? logger = null) where T : class
    {
        try
        {
            var data = System.Text.Json.JsonSerializer.Serialize(value);
            var options = new DistributedCacheEntryOptions();

            if (expiration.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = expiration;
            }

            await cache.SetStringAsync(key, data, options);
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
        ILogger? logger = null)
    {
        try
        {
            await cache.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to remove key {Key} from cache", key);
        }
    }
}
