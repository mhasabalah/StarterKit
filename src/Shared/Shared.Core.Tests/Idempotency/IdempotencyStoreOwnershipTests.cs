using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using Shared.Core.Web.Idempotency;
using Testcontainers.Redis;

namespace Shared.Core.Tests.Idempotency;

/// <summary>
/// Complete/Release are tied to the reserving request: after the in-flight TTL expired and a newer request reserved the
/// key, the stale request can neither overwrite nor delete the newer reservation.
/// </summary>
public abstract class IdempotencyStoreOwnershipTests
{
    protected abstract IIdempotencyStore Store { get; }
    protected static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    protected static IdempotencyEntry Response(string fingerprint = "fp") =>
        new(true, fingerprint, 200, "application/json", null, [1, 2, 3]);

    /// <summary>Simulates the in-flight TTL expiring.</summary>
    protected abstract Task ExpireAsync(string key);

    [Fact]
    public async Task Owner_can_complete_and_the_response_is_replayed()
    {
        var key = $"k-{Guid.NewGuid():N}";
        var reservation = await Store.TryReserveAsync(key, "fp", Ttl, default);

        (await Store.CompleteAsync(key, reservation.Owner!, Response(), Ttl, default)).Should().BeTrue();

        var again = await Store.TryReserveAsync(key, "fp", Ttl, default);
        again.Status.Should().Be(IdempotencyReservationStatus.Completed);
        again.Entry!.Body.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Stale_request_cannot_complete_over_a_newer_reservation()
    {
        var key = $"k-{Guid.NewGuid():N}";
        var stale = await Store.TryReserveAsync(key, "fp", Ttl, default);
        await ExpireAsync(key);
        var fresh = await Store.TryReserveAsync(key, "fp", Ttl, default);
        fresh.Status.Should().Be(IdempotencyReservationStatus.Reserved);

        (await Store.CompleteAsync(key, stale.Owner!, Response("stale"), Ttl, default)).Should().BeFalse();

        (await Store.TryReserveAsync(key, "fp", Ttl, default)).Status.Should().Be(IdempotencyReservationStatus.InProgress,
            "the newer reservation is untouched");
        (await Store.CompleteAsync(key, fresh.Owner!, Response(), Ttl, default)).Should().BeTrue();
    }

    [Fact]
    public async Task Stale_request_cannot_release_a_newer_reservation()
    {
        var key = $"k-{Guid.NewGuid():N}";
        var stale = await Store.TryReserveAsync(key, "fp", Ttl, default);
        await ExpireAsync(key);
        var fresh = await Store.TryReserveAsync(key, "fp", Ttl, default);

        (await Store.ReleaseAsync(key, stale.Owner!, default)).Should().BeFalse();

        (await Store.TryReserveAsync(key, "fp", Ttl, default)).Status.Should().Be(IdempotencyReservationStatus.InProgress);
        (await Store.ReleaseAsync(key, fresh.Owner!, default)).Should().BeTrue();
        (await Store.TryReserveAsync(key, "fp", Ttl, default)).Status.Should().Be(IdempotencyReservationStatus.Reserved);
    }

    [Fact]
    public async Task Release_never_deletes_a_completed_response()
    {
        var key = $"k-{Guid.NewGuid():N}";
        var reservation = await Store.TryReserveAsync(key, "fp", Ttl, default);
        await Store.CompleteAsync(key, reservation.Owner!, Response(), Ttl, default);

        (await Store.ReleaseAsync(key, reservation.Owner!, default)).Should().BeFalse();

        (await Store.TryReserveAsync(key, "fp", Ttl, default)).Status.Should().Be(IdempotencyReservationStatus.Completed);
    }
}

public sealed class DistributedCacheIdempotencyStoreOwnershipTests : IdempotencyStoreOwnershipTests
{
    private readonly MemoryDistributedCache _cache =
        new(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));

    private IIdempotencyStore? _store;
    protected override IIdempotencyStore Store => _store ??= new DistributedCacheIdempotencyStore(_cache);

    protected override Task ExpireAsync(string key) => _cache.RemoveAsync(key);
}

public sealed class RedisIdempotencyStoreOwnershipTests : IdempotencyStoreOwnershipTests, IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();
    private ConnectionMultiplexer _redis = null!;
    private IIdempotencyStore? _store;

    protected override IIdempotencyStore Store => _store!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        _store = new RedisIdempotencyStore(_redis, "test:");
    }

    public async Task DisposeAsync()
    {
        await _redis.DisposeAsync();
        await _container.DisposeAsync();
    }

    protected override Task ExpireAsync(string key) => _redis.GetDatabase().KeyDeleteAsync("test:" + key);
}
