using MsOptions = Microsoft.Extensions.Options.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;

namespace Shared.Core.Tests.MultiTenancy;

public sealed class TenantScopeTests
{
    [Fact]
    public void Single_tenant_mode_defaults_to_the_Default_tenant()
    {
        TenantContext.SingleTenant.Tenant.Should().Be(TenantDefaults.DefaultTenant);
        TenantContext.SingleTenant.IsHostScope.Should().BeFalse();
    }

    [Fact]
    public void Multi_tenant_mode_has_no_tenant_outside_a_scope()
    {
        MultiTenantContext().Tenant.Should().BeNull();
    }

    [Fact]
    public void Scopes_nest_and_restore()
    {
        var context = MultiTenantContext();

        using (TenantScope.Begin(TenantA))
        {
            context.TenantId.Should().Be(TenantA.Id);

            using (TenantScope.BeginHostScope())
            {
                context.IsHostScope.Should().BeTrue();
                context.TenantId.Should().BeNull();
            }

            context.TenantId.Should().Be(TenantA.Id);
            context.IsHostScope.Should().BeFalse();
        }

        context.Tenant.Should().BeNull();
    }

    [Fact]
    public async Task The_ambient_tenant_flows_into_async_work()
    {
        var context = MultiTenantContext();
        Guid? seen = null;

        using (TenantScope.Begin(TenantB))
        {
            await Task.Run(async () =>
            {
                await Task.Yield();
                seen = context.TenantId;
            });
        }

        seen.Should().Be(TenantB.Id);
    }

    [Fact]
    public void Host_scope_can_target_a_tenant_database()
    {
        var context = MultiTenantContext();
        using (TenantScope.BeginHostScope(TenantA))
        {
            context.IsHostScope.Should().BeTrue();
            context.TenantId.Should().Be(TenantA.Id);
        }
    }
}

public sealed class TenantSeedIdsTests
{
    private static readonly Guid Seed = Guid.Parse("30000000-0000-0000-0000-000000000001");

    [Fact]
    public void Default_tenant_keeps_the_well_known_seed_id() =>
        TenantSeedIds.For(TenantDefaults.DefaultTenantId, Seed).Should().Be(Seed);

    [Fact]
    public void Other_tenants_get_stable_distinct_ids()
    {
        var a = TenantSeedIds.For(TenantA.Id, Seed);
        a.Should().Be(TenantSeedIds.For(TenantA.Id, Seed), "seeding must stay idempotent");
        a.Should().NotBe(Seed);
        a.Should().NotBe(TenantSeedIds.For(TenantB.Id, Seed));
    }
}

public sealed class TenantConnectionStringResolverTests
{
    private const string Shared = "Host=db;Database=app;Username=u;Password=p";

    private static TenantConnectionStringResolver Resolver(bool enabled, Dictionary<string, string?>? settings = null) =>
        new(MsOptions.Create(new MultiTenancyOptions { Enabled = enabled }),
            new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build());

    [Fact]
    public void Shared_tenants_use_the_shared_database() =>
        Resolver(true).Resolve(TenantA, Shared).Should().Be(Shared);

    [Fact]
    public void Disabled_mode_always_uses_the_shared_database() =>
        Resolver(false).Resolve(TenantA with { Isolation = TenantIsolation.Dedicated }, Shared).Should().Be(Shared);

    [Fact]
    public void Dedicated_tenant_without_ref_gets_its_own_database_on_the_shared_server()
    {
        var cs = Resolver(true).Resolve(TenantA with { Isolation = TenantIsolation.Dedicated }, Shared);
        cs.Should().Contain("Database=app_t_acme").And.Contain("Host=db").And.Contain("Maximum Pool Size=20");
    }

    [Fact]
    public void Dedicated_tenant_with_ref_uses_the_configured_connection()
    {
        var resolver = Resolver(true, new() { ["MultiTenancy:Connections:acme-db"] = "Host=other;Database=acme" });
        resolver.Resolve(TenantA with { Isolation = TenantIsolation.Dedicated, ConnectionSecretRef = "acme-db" }, Shared)
            .Should().Contain("Host=other");
    }

    [Fact]
    public void Dedicated_tenant_with_a_missing_ref_fails_instead_of_falling_back_to_shared()
    {
        var act = () => Resolver(true).Resolve(
            TenantA with { Isolation = TenantIsolation.Dedicated, ConnectionSecretRef = "missing" }, Shared);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("app", "Acme-Co", "app_t_acme_co")]
    [InlineData("app", "x;DROP", "app_t_x_drop")]
    public void Database_names_are_sanitized(string db, string identifier, string expected) =>
        TenantConnectionStringResolver.DedicatedDatabaseName(db, identifier, "{database}_t_{identifier}")
            .Should().Be(expected);
}

public sealed class TenantCacheKeysTests
{
    [Fact]
    public void Keys_are_prefixed_with_the_tenant()
    {
        var keys = new TenantCacheKeys(MultiTenantContext());
        using (TenantScope.Begin(TenantA))
        {
            keys.For("catalog:tree").Should().Be($"t:{TenantA.Id:N}:catalog:tree");
        }
    }

    [Fact]
    public void Tenant_keys_without_a_tenant_fail_closed()
    {
        var keys = new TenantCacheKeys(MultiTenantContext());
        FluentActions.Invoking(() => keys.For("x")).Should().Throw<TenantNotResolvedException>();
    }
}
