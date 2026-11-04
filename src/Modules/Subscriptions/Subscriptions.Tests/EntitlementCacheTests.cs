using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Core.MultiTenancy;
using Subscriptions.Domains.Subscriptions.Entitlements;
using Subscriptions.Options;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Subscriptions.Tests;

/// <summary>
/// The entitlement cache is keyed by the globally unique subscriber id, so a webhook or job without an ambient tenant
/// still invalidates what a tenant request cached.
/// </summary>
public class EntitlementCacheTests
{
    private static EntitlementCache NewCache(IDistributedCache store) =>
        new(store, MsOptions.Create(new SubscriptionOptions()), NullLogger<EntitlementCache>.Instance);

    [Fact]
    public async Task Invalidate_WithoutAnAmbientTenant_DropsTheEntryCachedInsideATenant()
    {
        var store = new MemoryDistributedCache(MsOptions.Create(new MemoryDistributedCacheOptions()));
        var cache = NewCache(store);
        var subscriber = Guid.NewGuid();
        var snapshot = new EntitlementSnapshot(5, null, null, new Dictionary<string, string> { ["x"] = "1" });
        var tenant = new TenantInfo(Guid.NewGuid(), "acme", "Acme");

        using (TenantScope.Begin(tenant))
        {
            await cache.SetAsync(subscriber, snapshot);
            (await cache.GetAsync(subscriber)).Should().NotBeNull();
        }

        // Anonymous webhook / background job: no tenant scope at all.
        await cache.InvalidateAsync(subscriber);

        using (TenantScope.Begin(tenant))
        {
            (await cache.GetAsync(subscriber)).Should().BeNull();
        }
    }

    [Fact]
    public async Task Invalidate_WithoutAnAmbientTenant_DoesNotThrow()
    {
        var cache = NewCache(new MemoryDistributedCache(MsOptions.Create(new MemoryDistributedCacheOptions())));

        var act = () => cache.InvalidateAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }
}
