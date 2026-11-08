using Microsoft.EntityFrameworkCore;
using Shared.Core.Exceptions;
using Tenancy.Contracts.FeatureFlags;
using Tenancy.Contracts.Tenants;

namespace Tenancy.IntegrationTests;

/// <summary>Child-only changes move the version of their aggregate, through the real pipeline (ADR 0021).</summary>
[Collection(TenancyPostgresCollection.Name)]
public sealed class ChildCollectionVersionTests(TenancyPostgresFixture fx)
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static string NewFlagKey() => $"flag-{Guid.NewGuid():N}"[..24];

    private Task<long> FlagVersion(string key) =>
        fx.WithDb(db => db.FeatureFlags.AsNoTracking().Where(f => f.Key == key).Select(f => f.Version).SingleAsync());

    private Task<long> TenantVersion(Guid id) =>
        fx.WithDb(db => db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(t => t.Version).SingleAsync());

    [Fact]
    public async Task Setting_and_removing_an_override_each_move_the_flag_version()
    {
        var key = NewFlagKey();
        await fx.SendAs(Admin, new CreateFeatureFlagCommand(key, "test", false, 0, false));

        var set = await fx.SendAs(Admin, new SetFeatureFlagOverrideCommand(key, Guid.NewGuid(), null, true));
        (await FlagVersion(key)).Should().Be(1);

        await fx.SendAs(Admin, new RemoveFeatureFlagOverrideCommand(key, set.OverrideId));
        (await FlagVersion(key)).Should().Be(2);
    }

    [Fact]
    public async Task A_flag_update_with_the_version_from_before_an_override_change_is_rejected()
    {
        var key = NewFlagKey();
        await fx.SendAs(Admin, new CreateFeatureFlagCommand(key, "test", false, 0, false));
        await fx.SendAs(Admin, new SetFeatureFlagOverrideCommand(key, Guid.NewGuid(), null, true));

        var stale = () => fx.SendAs(Admin, new UpdateFeatureFlagCommand(key, "stale", true, 0, false, Version: 0));

        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Adding_and_removing_a_custom_domain_each_move_the_tenant_version()
    {
        var owner = Guid.NewGuid();
        var created = await fx.SendAs(owner, new CreateTenantCommand("Acme", $"t-{Guid.NewGuid():N}"[..20]));
        var before = await TenantVersion(created.TenantId);
        var host = $"{Guid.NewGuid():N}.example.org";

        await fx.SendInTenant(created.TenantId, owner, new AddTenantDomainCommand(host));
        (await TenantVersion(created.TenantId)).Should().Be(before + 1);

        await fx.SendInTenant(created.TenantId, owner, new RemoveTenantDomainCommand(host));
        (await TenantVersion(created.TenantId)).Should().Be(before + 2);
    }
}
