using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.Core.Options;
using StackExchange.Redis;

namespace Shared.Core.Web.Idempotency;

public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the idempotency store: Redis (<c>SET NX</c>, atomic across instances) when Redis is enabled,
    /// otherwise the in-process <see cref="IDistributedCache"/> fallback. Call after <c>AddRedisCache</c>.
    /// </summary>
    public static IServiceCollection AddIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .Validate(o => o.Ttl > TimeSpan.Zero && o.InFlightTtl > TimeSpan.Zero,
                "Idempotency:Ttl and Idempotency:InFlightTtl must be positive.")
            .ValidateOnStart();

        services.TryAddSingleton<IIdempotencyStore>(sp =>
        {
            var redis = sp.GetService<IConnectionMultiplexer>();
            if (redis is not null)
            {
                var prefix = sp.GetService<IOptions<RedisOptions>>()?.Value.InstanceName ?? string.Empty;
                return new RedisIdempotencyStore(redis, prefix);
            }

            return new DistributedCacheIdempotencyStore(sp.GetRequiredService<IDistributedCache>());
        });
        return services;
    }

    /// <summary>Adds the body-fingerprint middleware; place it before endpoints are mapped.</summary>
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyBodyFingerprintMiddleware>();
}
