using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Caching;
using Tenancy.Services;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tenancy.Tests;

/// <summary>Tenant/membership invalidations are evicted locally AND broadcast so other instances drop their L1 entry.</summary>
public sealed class CacheInvalidationBroadcastTests
{
    private readonly Mock<ILocalCacheInvalidationBus> _bus = new();
    private readonly MemoryCache _memory = new(new MemoryCacheOptions());
    private readonly Mock<IDistributedCache> _distributed = new();

    [Fact]
    public async Task Tenant_invalidation_evicts_locally_and_broadcasts_every_lookup_key()
    {
        var id = Guid.NewGuid();
        _memory.Set($"tenancy:tenant:id:{id:N}", "x");
        var store = new TenantStore(Mock.Of<IUnitOfWorkRunner>(), _memory, _distributed.Object,
            MsOptions.Create(new MultiTenancyOptions()), _bus.Object, NullLogger<TenantStore>.Instance);

        await store.InvalidateAsync(id, "acme", ["acme.example.com"]);

        _memory.TryGetValue($"tenancy:tenant:id:{id:N}", out _).Should().BeFalse();
        _bus.Verify(b => b.PublishAsync(
            It.Is<IReadOnlyCollection<string>>(k => k.SequenceEqual(new[]
            {
                $"tenancy:tenant:id:{id:N}", "tenancy:tenant:ident:acme", "tenancy:tenant:host:acme.example.com"
            })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tenant_invalidation_still_evicts_locally_when_the_distributed_cache_fails()
    {
        var id = Guid.NewGuid();
        _distributed.Setup(d => d.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));
        var store = new TenantStore(Mock.Of<IUnitOfWorkRunner>(), _memory, _distributed.Object,
            MsOptions.Create(new MultiTenancyOptions()), _bus.Object, NullLogger<TenantStore>.Instance);

        var act = () => store.InvalidateAsync(id, null, []);

        await act.Should().NotThrowAsync();
        _bus.Verify(b => b.PublishAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tenant_wide_membership_invalidation_broadcasts_the_generation_key()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new TenantMembershipResolver(Mock.Of<IUnitOfWorkRunner>(), _memory, _distributed.Object,
            MsOptions.Create(new MultiTenancyOptions()), _bus.Object, NullLogger<TenantMembershipResolver>.Instance);

        await resolver.InvalidateAsync(tenantId);

        _bus.Verify(b => b.PublishAsync(
            It.Is<IReadOnlyCollection<string>>(k => k.Single() == $"tenancy:mgen:{tenantId:N}"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task User_membership_invalidation_broadcasts_the_entry_key()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var resolver = new TenantMembershipResolver(Mock.Of<IUnitOfWorkRunner>(), _memory, _distributed.Object,
            MsOptions.Create(new MultiTenancyOptions()), _bus.Object, NullLogger<TenantMembershipResolver>.Instance);

        await resolver.InvalidateAsync(tenantId, userId);

        _bus.Verify(b => b.PublishAsync(
            It.Is<IReadOnlyCollection<string>>(k => k.Single().StartsWith($"tenancy:m:{tenantId:N}:{userId:N}:")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Tenant_invalidation_removes_the_distributed_entry_before_broadcasting()
    {
        var order = new List<string>();
        _distributed.Setup(d => d.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("l2")).Returns(Task.CompletedTask);
        _bus.Setup(b => b.PublishAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("publish")).Returns(Task.CompletedTask);
        var store = new TenantStore(Mock.Of<IUnitOfWorkRunner>(), _memory, _distributed.Object,
            MsOptions.Create(new MultiTenancyOptions()), _bus.Object, NullLogger<TenantStore>.Instance);

        await store.InvalidateAsync(Guid.NewGuid(), "acme", ["acme.example.com"]);

        order.Should().Equal("l2", "l2", "l2", "publish");
    }

    [Fact]
    public async Task User_membership_invalidation_removes_the_distributed_entry_before_broadcasting()
    {
        var order = new List<string>();
        _distributed.Setup(d => d.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("l2")).Returns(Task.CompletedTask);
        _bus.Setup(b => b.PublishAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("publish")).Returns(Task.CompletedTask);
        var resolver = new TenantMembershipResolver(Mock.Of<IUnitOfWorkRunner>(), _memory, _distributed.Object,
            MsOptions.Create(new MultiTenancyOptions()), _bus.Object, NullLogger<TenantMembershipResolver>.Instance);

        await resolver.InvalidateAsync(Guid.NewGuid(), Guid.NewGuid());

        order.Should().Equal("l2", "publish");
    }
}
