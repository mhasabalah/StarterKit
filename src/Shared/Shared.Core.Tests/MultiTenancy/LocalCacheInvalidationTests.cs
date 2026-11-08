using Moq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Shared.Core.Tests.MultiTenancy;

public sealed class LocalCacheInvalidationTests
{
    private static RedisLocalCacheInvalidationBus Bus(IConnectionMultiplexer redis, IMemoryCache cache,
        MultiTenancyOptions? options = null) =>
        new(redis, cache, MsOptions.Create(options ?? new MultiTenancyOptions()),
            NullLogger<RedisLocalCacheInvalidationBus>.Instance);

    [Fact]
    public void Received_message_evicts_exactly_the_listed_local_entries()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3);

        Bus(new Mock<IConnectionMultiplexer>().Object, cache).Evict("[\"a\",\"b\",\"missing\"]");

        cache.TryGetValue("a", out _).Should().BeFalse();
        cache.TryGetValue("b", out _).Should().BeFalse();
        cache.TryGetValue("c", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"a\":1}")]
    public void Malformed_message_is_ignored_and_evicts_nothing(string payload)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("a", 1);

        var act = () => Bus(new Mock<IConnectionMultiplexer>().Object, cache).Evict(payload);

        act.Should().NotThrow();
        cache.TryGetValue("a", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Publish_failure_never_throws()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var act = () => Bus(redis.Object, cache).PublishAsync(["a"]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Start_failure_never_throws()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetSubscriber(It.IsAny<object>())).Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var act = () => Bus(redis.Object, cache).StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Without_redis_the_bus_is_a_no_op_and_the_hosted_service_is_harmless()
    {
        var services = new ServiceCollection().AddLogging().AddMultiTenancyCore();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ILocalCacheInvalidationBus>().Should().BeOfType<NoOpLocalCacheInvalidationBus>();
        provider.GetServices<IHostedService>().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Broadcast_can_be_switched_off()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = new MultiTenancyOptions { Cache = { BroadcastInvalidation = false } };

        var bus = Bus(redis.Object, cache, options);
        await bus.StartAsync(CancellationToken.None);
        await bus.PublishAsync(["a"]);

        redis.VerifyNoOtherCalls();
    }
}

/// <summary>Two "instances" (own L1 each) over one real Redis: an invalidation on one evicts the other's entry.</summary>
public sealed class LocalCacheInvalidationRedisTests : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();
    private IConnectionMultiplexer _redis = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Invalidation_on_one_instance_evicts_the_entry_on_the_other()
    {
        using var cacheA = new MemoryCache(new MemoryCacheOptions());
        using var cacheB = new MemoryCache(new MemoryCacheOptions());
        var options = MsOptions.Create(new MultiTenancyOptions());
        var busA = new RedisLocalCacheInvalidationBus(_redis, cacheA, options, NullLogger<RedisLocalCacheInvalidationBus>.Instance);
        var busB = new RedisLocalCacheInvalidationBus(_redis, cacheB, options, NullLogger<RedisLocalCacheInvalidationBus>.Instance);
        await busA.StartAsync(CancellationToken.None);
        await busB.StartAsync(CancellationToken.None);
        cacheA.Set("tenancy:tenant:id:1", "active");
        cacheB.Set("tenancy:tenant:id:1", "active");
        cacheB.Set("other", "kept");

        cacheA.Remove("tenancy:tenant:id:1");
        await busA.PublishAsync(["tenancy:tenant:id:1"]);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (cacheB.TryGetValue("tenancy:tenant:id:1", out _) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        cacheB.TryGetValue("tenancy:tenant:id:1", out _).Should().BeFalse("the broadcast reached the other instance");
        cacheB.TryGetValue("other", out _).Should().BeTrue();

        await busA.StopAsync(CancellationToken.None);
        await busB.StopAsync(CancellationToken.None);
    }
}
