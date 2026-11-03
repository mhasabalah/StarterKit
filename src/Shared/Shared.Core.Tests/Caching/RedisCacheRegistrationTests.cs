using Microsoft.Extensions.Caching.Distributed;
using Moq;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shared.Core.Extensions;
using Shared.Core.HealthCheck;
using StackExchange.Redis;

namespace Shared.Core.Tests.Caching;

/// <summary>DI shape of AddRedisCache. No Redis server: nothing here opens a connection.</summary>
public class RedisCacheRegistrationTests
{
    private static IServiceCollection Services(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ASPNETCORE_ENVIRONMENT"] = "Development" }
                .Concat(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddRedisCache(configuration);
        return services;
    }

    [Fact]
    public void Disabled_redis_falls_back_to_the_memory_cache_without_a_multiplexer_or_health_check()
    {
        using var provider = Services(("Redis:Enabled", "false")).BuildServiceProvider();

        provider.GetRequiredService<IDistributedCache>().Should().BeOfType<MemoryDistributedCache>();
        provider.GetService<IConnectionMultiplexer>().Should().BeNull();
        provider.GetService<IOptions<HealthCheckServiceOptions>>()?.Value.Registrations
            .Should().NotContain(r => r.Name == RedisExtensions.HealthCheckName);
    }

    [Fact]
    public void Enabled_redis_shares_one_multiplexer_between_the_cache_and_other_consumers()
    {
        var services = Services(("ConnectionStrings:redis", "localhost:6379"));

        services.Should().ContainSingle(d => d.ServiceType == typeof(IConnectionMultiplexer))
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var cacheOptions = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        cacheOptions.ConnectionMultiplexerFactory.Should()
            .NotBeNull("RedisCache must reuse the shared multiplexer instead of opening its own connection");
        cacheOptions.InstanceName.Should().Be("StarterKit:");
    }

    [Fact]
    public void Enabled_redis_registers_a_readiness_health_check()
    {
        using var provider = Services(("ConnectionStrings:redis", "localhost:6379")).BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations
            .Should().ContainSingle(r => r.Name == RedisExtensions.HealthCheckName).Subject;
        registration.Tags.Should().Contain(HealthCheckTags.Ready).And.NotContain(HealthCheckTags.Live);
    }

    [Fact]
    public void A_consumer_registering_its_own_multiplexer_first_keeps_it()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ConnectionStrings:redis"] = "localhost:6379"
            })
            .Build();
        var existing = new Mock<IConnectionMultiplexer>().Object;
        var services = new ServiceCollection();
        services.AddSingleton(existing);

        services.AddRedisCache(configuration);

        services.Should().ContainSingle(d => d.ServiceType == typeof(IConnectionMultiplexer))
            .Which.ImplementationInstance.Should().BeSameAs(existing);
    }

    [Fact]
    public void Instance_name_suffix_is_appended_to_the_key_prefix()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ConnectionStrings:redis"] = "localhost:6379",
                ["Redis:InstanceName"] = "App:"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddRedisCache(configuration, "TokenBlacklist");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.InstanceName.Should().Be("App:TokenBlacklist:");
    }
}
