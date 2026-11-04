using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Tenants;
using Tenancy.Data;
using Tenancy.Domains.Members.Consumers;
using Tenancy.Domains.Members.Models;
using Tenancy.Domains.Tenants.Features;
using Tenancy.Domains.Tenants.Models;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Tenancy.Tests;

/// <summary>Account erasure on the Tenancy side: owned-tenant lookup (blocks deletion) and membership removal.</summary>
public sealed class UserDeletedConsumerTests
{
    private readonly string _database = $"user-deleted-{Guid.NewGuid():N}";
    private readonly Mock<IDomainEventDispatcher> _dispatcher = new();

    private TenancyDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseInMemoryDatabase(_database).Options);

    private async Task<Tenant> SeedTenantAsync(Guid owner, Action<Tenant>? configure = null)
    {
        var tenant = Tenant.Create($"t{Guid.NewGuid():N}"[..12], "Acme", owner, TenantIsolation.Shared);
        configure?.Invoke(tenant);
        await using var db = NewContext();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task SeedMembershipAsync(Guid tenantId, Guid userId, bool owner = false)
    {
        await using var db = NewContext();
        using (TenantScope.BeginHostScope())
        {
            db.Memberships.Add(TenantMembership.Create(tenantId, userId, [], [], DateTime.UtcNow, owner));
            await db.SaveChangesAsync();
        }
    }

    private async Task<List<Guid>> MemberIdsAsync()
    {
        await using var db = NewContext();
        using (TenantScope.BeginHostScope())
        {
            return await db.Memberships.AsNoTracking().Select(m => m.UserId).ToListAsync();
        }
    }

    private async Task ConsumeAsync(Guid userId)
    {
        var context = new Mock<ConsumeContext<UserDeletedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new UserDeletedIntegrationEvent { UserId = userId });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await using var db = NewContext();
        await new UserDeletedTenancyConsumer(db, _dispatcher.Object, NullLogger<UserDeletedTenancyConsumer>.Instance)
            .Consume(context.Object);
    }

    [Fact]
    public async Task Consume_RemovesTheUsersMembershipsInEveryTenant_AndLeavesOtherUsersAlone()
    {
        var user = Guid.NewGuid();
        var other = Guid.NewGuid();
        var a = await SeedTenantAsync(Guid.NewGuid());
        var b = await SeedTenantAsync(Guid.NewGuid());
        await SeedMembershipAsync(a.Id, user);
        await SeedMembershipAsync(b.Id, user);
        await SeedMembershipAsync(a.Id, other);

        await ConsumeAsync(user);

        (await MemberIdsAsync()).Should().Equal(other);
        _dispatcher.Verify(d => d.DispatchPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_Twice_IsANoOpTheSecondTime()
    {
        var user = Guid.NewGuid();
        var tenant = await SeedTenantAsync(Guid.NewGuid());
        await SeedMembershipAsync(tenant.Id, user);

        await ConsumeAsync(user);
        await ConsumeAsync(user);

        (await MemberIdsAsync()).Should().BeEmpty();
        _dispatcher.Verify(d => d.DispatchPendingAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_StillRemovesAMembershipThatBecameAnOwnerMeanwhile()
    {
        var user = Guid.NewGuid();
        var tenant = await SeedTenantAsync(user);
        await SeedMembershipAsync(tenant.Id, user, owner: true);

        await ConsumeAsync(user);

        (await MemberIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task OwnedTenants_ListsOnlyLiveTenantsOfThatOwner()
    {
        var owner = Guid.NewGuid();
        var live = await SeedTenantAsync(owner);
        await SeedTenantAsync(owner, t => t.RequestDeletion(DateTime.UtcNow, 30, "closing"));
        await SeedTenantAsync(Guid.NewGuid());

        await using var db = NewContext();
        var owned = await new GetOwnedTenantsHandler(db).Handle(new GetOwnedTenantsQuery(owner), CancellationToken.None);

        owned.Should().ContainSingle().Which.TenantId.Should().Be(live.Id);
    }

    [Fact]
    public async Task OwnedTenants_ForAUserWhoOwnsNothing_IsEmpty()
    {
        await SeedTenantAsync(Guid.NewGuid());

        await using var db = NewContext();
        var owned = await new GetOwnedTenantsHandler(db).Handle(new GetOwnedTenantsQuery(Guid.NewGuid()), CancellationToken.None);

        owned.Should().BeEmpty();
    }
}
