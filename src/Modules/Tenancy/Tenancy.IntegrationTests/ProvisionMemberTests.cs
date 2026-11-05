using Microsoft.EntityFrameworkCore;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Tenants;
using Tenancy.Domains.Roles.Models;
using Tenancy.Domains.Audit.Models;
using Tenancy.Domains.Tenants.Exceptions;

namespace Tenancy.IntegrationTests;

/// <summary>
/// Directory provisioning (<see cref="ProvisionMemberCommand"/>, sent by Identity for SSO JIT and SCIM) on real
/// PostgreSQL: idempotency, role filtering, suspend/reactivate, the owner break-glass rule and the plan seat limit.
/// </summary>
[Collection(TenancyPostgresCollection.Name)]
public sealed class ProvisionMemberTests(TenancyPostgresFixture fx) : IAsyncLifetime
{
    private Guid _tenantId;
    private Guid _ownerId;

    public async Task InitializeAsync()
    {
        _ownerId = Guid.NewGuid();
        var created = await fx.SendAs(_ownerId, new CreateTenantCommand("Directory Co", $"d-{Guid.NewGuid():N}"[..20]));
        _tenantId = created.TenantId;
        fx.PlanLimits.MaxUsers = null;
    }

    public Task DisposeAsync()
    {
        fx.PlanLimits.MaxUsers = null;
        return Task.CompletedTask;
    }

    private Task<ProvisionMemberResult> Provision(Guid userId, IReadOnlyList<Guid>? roles = null, bool? active = null) =>
        fx.SendInTenant(_tenantId, _ownerId, new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim, roles, active));

    private Task<int> AuditCount(Guid userId, string action) => fx.WithDb(db =>
        db.AuditLogs.CountAsync(a => a.TenantId == _tenantId && a.TargetId == userId.ToString() && a.Action == action));

    [Fact]
    public async Task New_user_becomes_an_active_member_with_known_roles_only_and_a_retry_changes_nothing()
    {
        var userId = Guid.NewGuid();
        var member = SystemTenantRoles.IdOf(_tenantId, SystemTenantRoles.Member);

        var first = await Provision(userId, [member, Guid.NewGuid()]);
        var retry = await Provision(userId, [member, Guid.NewGuid()]);

        first.Created.Should().BeTrue();
        first.Status.Should().Be(TenantMembershipStatus.Active);
        retry.Created.Should().BeFalse();
        var membership = await fx.WithDb(db => db.Memberships.SingleAsync(m => m.TenantId == _tenantId && m.UserId == userId));
        membership.RoleIds.Should().Equal([member], "role ids that are not roles of this tenant are dropped");
        (await AuditCount(userId, TenantAuditActions.MemberProvisioned)).Should().Be(1, "an idempotent retry is not audited");
    }

    [Fact]
    public async Task Deactivation_suspends_and_reactivation_restores_the_membership()
    {
        var userId = Guid.NewGuid();
        await Provision(userId);

        (await Provision(userId, active: false)).Status.Should().Be(TenantMembershipStatus.Suspended);
        (await AuditCount(userId, TenantAuditActions.MemberDeprovisioned)).Should().Be(1);

        (await Provision(userId, active: true)).Status.Should().Be(TenantMembershipStatus.Active);
        (await fx.WithDb(db => db.Memberships.CountAsync(m => m.TenantId == _tenantId && m.UserId == userId)))
            .Should().Be(1, "deprovisioning never deletes the membership");
    }

    [Fact]
    public async Task Deactivating_someone_who_never_joined_creates_nothing()
    {
        var userId = Guid.NewGuid();

        var result = await Provision(userId, active: false);

        result.Created.Should().BeFalse();
        (await fx.WithDb(db => db.Memberships.AnyAsync(m => m.UserId == userId))).Should().BeFalse();
    }

    [Fact]
    public async Task A_directory_never_suspends_the_tenant_owner()
    {
        var result = await Provision(_ownerId, active: false);

        result.Status.Should().Be(TenantMembershipStatus.Active);
        var owner = await fx.WithDb(db => db.Memberships.SingleAsync(m => m.TenantId == _tenantId && m.UserId == _ownerId));
        owner.Status.Should().Be(TenantMembershipStatus.Active);
    }

    [Fact]
    public async Task The_plan_seat_limit_is_enforced_and_nothing_is_written()
    {
        fx.PlanLimits.MaxUsers = 1; // the owner already holds the only seat
        var userId = Guid.NewGuid();

        var act = () => Provision(userId);

        await act.Should().ThrowAsync<TenantLimitExceededException>();
        (await fx.WithDb(db => db.Memberships.AnyAsync(m => m.UserId == userId))).Should().BeFalse();
    }

    [Fact]
    public async Task Reactivation_also_needs_a_free_seat()
    {
        var userId = Guid.NewGuid();
        await Provision(userId);
        await Provision(userId, active: false);
        fx.PlanLimits.MaxUsers = 1;

        var act = () => Provision(userId, active: true);

        await act.Should().ThrowAsync<TenantLimitExceededException>();
        var membership = await fx.WithDb(db => db.Memberships.SingleAsync(m => m.TenantId == _tenantId && m.UserId == userId));
        membership.Status.Should().Be(TenantMembershipStatus.Suspended);
    }
}
