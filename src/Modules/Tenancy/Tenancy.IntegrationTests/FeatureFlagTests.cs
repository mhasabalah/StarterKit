using FluentValidation;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Contracts.FeatureFlags;
using Shared.Core.Data;
using Shared.Core.Exceptions;
using Shared.Core.FeatureFlags;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Caching;
using Tenancy.Contracts.FeatureFlags;
using Tenancy.Domains.FeatureFlags.Services;

namespace Tenancy.IntegrationTests;

/// <summary>Feature flags on real PostgreSQL with the real migrations and the real mediator pipeline.</summary>
[Collection(TenancyPostgresCollection.Name)]
public sealed class FeatureFlagTests(TenancyPostgresFixture fx)
{
    private static string Key() => $"ff-{Guid.NewGuid():N}"[..20];
    private static readonly Guid Admin = Guid.NewGuid();

    private Task<CreateFeatureFlagResult> Create(string key, bool enabled = false, int rollout = 0, bool kill = false) =>
        fx.SendAs(Admin, new CreateFeatureFlagCommand(key, "test", enabled, rollout, kill));

    private async Task<bool> IsOn(string key, Guid tenantId, Guid? userId)
    {
        using (TenantScope.Begin(new TenantInfo(tenantId, $"t-{tenantId:N}"[..12], "T")))
        {
            fx.Http.SetUser(userId);
            bool on = false;
            await fx.WithServices(async sp => on = await sp.GetRequiredService<IFeatureFlags>().IsEnabledAsync(key));
            return on;
        }
    }

    [Fact]
    public async Task Update_TakesEffectImmediately_BecauseTheCacheIsInvalidatedAfterTheCommit()
    {
        var key = Key();
        var tenant = Guid.NewGuid();
        await Create(key);
        (await IsOn(key, tenant, null)).Should().BeFalse(); // warms the cache

        await fx.SendAs(Admin, new UpdateFeatureFlagCommand(key, null, true, 0, false));

        (await IsOn(key, tenant, null)).Should().BeTrue();
    }

    [Fact]
    public async Task EvaluationOrder_KillSwitch_ThenUser_ThenTenant_ThenRollout_ThenDefault()
    {
        var key = Key();
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        await Create(key, enabled: false);
        await fx.SendAs(Admin, new SetFeatureFlagOverrideCommand(key, tenant, null, true));
        (await IsOn(key, tenant, Guid.NewGuid())).Should().BeTrue("tenant override");

        await fx.SendAs(Admin, new SetFeatureFlagOverrideCommand(key, null, user, false));
        (await IsOn(key, tenant, user)).Should().BeFalse("user override beats tenant override");
        (await IsOn(key, tenant, Guid.NewGuid())).Should().BeTrue("other users keep the tenant override");

        await fx.SendAs(Admin, new UpdateFeatureFlagCommand(key, null, false, 0, true));
        (await IsOn(key, tenant, Guid.NewGuid())).Should().BeFalse("kill switch beats overrides");

        await fx.SendAs(Admin, new UpdateFeatureFlagCommand(key, null, false, 100, false));
        (await IsOn(key, Guid.NewGuid(), null)).Should().BeTrue("100% rollout for tenants without override");
    }

    [Fact]
    public async Task Rollout_IsStablePerTenant_AcrossCacheReloads()
    {
        var key = Key();
        await Create(key, rollout: 50);
        var tenants = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToList();

        var first = new List<bool>();
        foreach (var t in tenants) first.Add(await IsOn(key, t, null));
        var second = new List<bool>();
        foreach (var t in tenants) second.Add(await IsOn(key, t, null));

        second.Should().Equal(first);
        first.Should().Contain(true).And.Contain(false);
    }

    [Fact]
    public async Task OverrideTargetingBothOrNeither_AndInvalidKey_AreRejected()
    {
        var key = Key();
        await Create(key);

        await fx.Awaiting(f => f.SendAs(Admin, new SetFeatureFlagOverrideCommand(key, Guid.NewGuid(), Guid.NewGuid(), true)))
            .Should().ThrowAsync<ValidationException>();
        await fx.Awaiting(f => f.SendAs(Admin, new SetFeatureFlagOverrideCommand(key, null, null, true)))
            .Should().ThrowAsync<ValidationException>();
        await fx.Awaiting(f => f.SendAs(Admin, new CreateFeatureFlagCommand("Bad Key!", null)))
            .Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DuplicateKey_IsAConflict_UnknownFlag_IsNotFound()
    {
        var key = Key();
        await Create(key);

        await fx.Awaiting(f => Create(key)).Should().ThrowAsync<ConflictException>();
        await fx.Awaiting(f => f.SendAs(Admin, new GetFeatureFlagByKeyQuery("nope-" + key))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Update_WithStaleVersion_IsRejected_AndNothingChanges()
    {
        var key = Key();
        await Create(key);
        var current = await fx.SendAs(Admin, new GetFeatureFlagByKeyQuery(key));
        await fx.SendAs(Admin, new UpdateFeatureFlagCommand(key, "v2", true, 0, false, current.Version));

        await fx.Awaiting(f => f.SendAs(Admin, new UpdateFeatureFlagCommand(key, "stale", false, 0, false, current.Version)))
            .Should().ThrowAsync<ConcurrencyConflictException>();

        (await fx.SendAs(Admin, new GetFeatureFlagByKeyQuery(key))).Description.Should().Be("v2");
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyFlagsThatAreOnForTheCaller_SoDarkLaunchedKeysDoNotLeak()
    {
        var on = Key();
        var dark = Key();
        var tenant = Guid.NewGuid();
        await Create(on, enabled: true);
        await Create(dark, enabled: false);

        using (TenantScope.Begin(new TenantInfo(tenant, $"t-{tenant:N}"[..12], "T")))
        {
            fx.Http.SetUser(Guid.NewGuid());
            IReadOnlyDictionary<string, bool> all = null!;
            await fx.WithServices(async sp => all = await sp.GetRequiredService<IFeatureFlags>().GetAllAsync());

            all.Should().ContainKey(on).WhoseValue.Should().BeTrue();
            all.Should().NotContainKey(dark);
            all.Values.Should().OnlyContain(v => v);
        }
    }

    [Fact]
    public async Task Delete_RemovesTheFlagAndItsOverrides_AndTheFlagFallsBackToConfiguredDefault()
    {
        var key = Key();
        var tenant = Guid.NewGuid();
        await Create(key, enabled: true);
        await fx.SendAs(Admin, new SetFeatureFlagOverrideCommand(key, tenant, null, true));
        (await IsOn(key, tenant, null)).Should().BeTrue();

        await fx.SendAs(Admin, new DeleteFeatureFlagCommand(key));

        (await IsOn(key, tenant, null)).Should().BeFalse();
    }

    [Fact]
    public async Task CacheFailure_NeverFailsTheRequest_TheDatabaseIsRead()
    {
        var key = Key();
        await Create(key, enabled: true);
        var broken = new Mock<IDistributedCache>();
        broken.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("redis down"));
        broken.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("redis down"));
        broken.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("redis down"));

        await fx.WithServices(async sp =>
        {
            var store = new FeatureFlagStore(sp.GetRequiredService<IUnitOfWorkRunner>(), new MemoryCache(new MemoryCacheOptions()),
                broken.Object, Options.Create(new MultiTenancyOptions()), new NoOpLocalCacheInvalidationBus(),
                NullLogger<FeatureFlagStore>.Instance);

            (await store.GetSnapshotAsync()).Should().ContainKey(key);
            await store.Awaiting(s => s.InvalidateAsync()).Should().NotThrowAsync();
        });
    }

    [Fact]
    public async Task StoreUnavailable_FallsBackToConfiguredDefaults_AndIsNotCached()
    {
        var failing = new FailingRunner();
        var memory = new MemoryCache(new MemoryCacheOptions());
        var store = new FeatureFlagStore(failing, memory, new MemoryDistributedCache(
                Options.Create(new MemoryDistributedCacheOptions())),
            Options.Create(new MultiTenancyOptions()), new NoOpLocalCacheInvalidationBus(), NullLogger<FeatureFlagStore>.Instance);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FeatureFlags:Defaults:fallback-flag"] = "true" }).Build();
        var tenantContext = new Mock<ITenantContext>();
        var http = new Mock<Shared.Core.Web.IHttpContextService>();
        var flags = new TenancyFeatureFlags(store, new FeatureFlagDefaults(config), tenantContext.Object, http.Object);

        (await flags.IsEnabledAsync("fallback-flag")).Should().BeTrue();
        (await flags.IsEnabledAsync("other")).Should().BeFalse();
        memory.TryGetValue(FeatureFlagStore.CacheKey, out _).Should().BeFalse("a failed read must not be cached");
    }

    [Fact]
    public async Task Snapshot_loaded_before_an_invalidation_is_not_cached_afterwards()
    {
        var memory = new MemoryCache(new MemoryCacheOptions());
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var runner = new BlockingRunner();
        var store = new FeatureFlagStore(runner, memory, distributed, Options.Create(new MultiTenancyOptions()),
            new NoOpLocalCacheInvalidationBus(), NullLogger<FeatureFlagStore>.Instance);

        var read = store.GetSnapshotAsync();          // starts loading the (soon stale) snapshot
        await runner.Started.Task;
        await store.InvalidateAsync();                // a flag changed while the reader was in the database
        runner.Release.SetResult();
        await read;

        memory.TryGetValue(FeatureFlagStore.CacheKey, out _).Should().BeFalse("the stale snapshot must not be re-cached in L1");
        (await distributed.GetStringAsync(FeatureFlagStore.CacheKey)).Should().BeNull("nor in L2");
    }

    [Fact]
    public async Task Snapshot_loaded_without_any_invalidation_is_cached()
    {
        var memory = new MemoryCache(new MemoryCacheOptions());
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var runner = new BlockingRunner();
        var store = new FeatureFlagStore(runner, memory, distributed, Options.Create(new MultiTenancyOptions()),
            new NoOpLocalCacheInvalidationBus(), NullLogger<FeatureFlagStore>.Instance);

        var read = store.GetSnapshotAsync();
        await runner.Started.Task;
        runner.Release.SetResult();
        await read;

        memory.TryGetValue(FeatureFlagStore.CacheKey, out _).Should().BeTrue();
        (await distributed.GetStringAsync(FeatureFlagStore.CacheKey)).Should().NotBeNull();
    }

    private sealed class BlockingRunner : IUnitOfWorkRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<TResult> RunAsync<TResult>(Func<IServiceProvider, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken = default)
        {
            Started.SetResult();
            await Release.Task;
            // An empty snapshot: the Snapshot record is private, so build it through reflection-free construction.
            return (TResult)Activator.CreateInstance(typeof(TResult), new object[] { new List<FeatureFlagDefinition>() })!;
        }
    }

    private sealed class FailingRunner : IUnitOfWorkRunner
    {
        public Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("db down");

        public Task<TResult> RunAsync<TResult>(Func<IServiceProvider, CancellationToken, Task<TResult>> work,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("db down");
    }
}
