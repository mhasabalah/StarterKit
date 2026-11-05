using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Tenancy.Contracts.Events;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Tenants;
using Tenancy.Domains.Branches.Models;
using Tenancy.Domains.Invitations.Models;
using Tenancy.Domains.Members.Models;
using Tenancy.Domains.Roles.Models;
using Tenancy.Domains.Tenants.Exceptions;
using Tenancy.Domains.Tenants.Models;

namespace Tenancy.Tests;

public sealed class TenantTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    [Theory]
    [InlineData("acme")]
    [InlineData("acme-co")]
    [InlineData("a1b")]
    public void Valid_identifiers_are_accepted(string identifier) =>
        Tenant.Create(identifier, "Acme", Owner, TenantIsolation.Shared).Identifier.Should().Be(identifier);

    [Theory]
    [InlineData("ab")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("ac me")]
    [InlineData("acme.co")]
    public void Invalid_identifiers_are_rejected(string identifier) =>
        FluentActions.Invoking(() => Tenant.Create(identifier, "Acme", Owner, TenantIsolation.Shared))
            .Should().Throw<InvalidTenantException>();

    [Fact]
    public void Reserved_identifiers_are_rejected() =>
        FluentActions.Invoking(() => Tenant.Create("admin", "Acme", Owner, TenantIsolation.Shared))
            .Should().Throw<InvalidTenantException>();

    [Fact]
    public void Identifiers_are_normalized() =>
        Tenant.Create("  ACME ", "Acme", Owner, TenantIsolation.Shared).Identifier.Should().Be("acme");

    [Fact]
    public void Shared_tenants_start_active_and_dedicated_ones_provisioning()
    {
        Tenant.Create("acme", "Acme", Owner, TenantIsolation.Shared).Status.Should().Be(TenantStatus.Active);
        var dedicated = Tenant.Create("big-co", "Big", Owner, TenantIsolation.Dedicated);
        dedicated.Status.Should().Be(TenantStatus.Provisioning);

        dedicated.MarkProvisioned();
        dedicated.Status.Should().Be(TenantStatus.Active);
        dedicated.DomainEvents.Should().ContainSingle(e => e is TenantActivatedEvent);
    }

    [Fact]
    public void Creation_raises_the_contract_event()
    {
        var tenant = Tenant.Create("acme", "Acme", Owner, TenantIsolation.Shared);
        tenant.DomainEvents.OfType<TenantCreatedEvent>().Single().OwnerUserId.Should().Be(Owner);
    }

    [Fact]
    public void Suspend_reactivate_lifecycle()
    {
        var tenant = Tenant.Create("acme", "Acme", Owner, TenantIsolation.Shared);
        tenant.Suspend("unpaid");
        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.StatusReason.Should().Be("unpaid");

        tenant.Reactivate();
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.StatusReason.Should().BeNull();
    }

    [Fact]
    public void Deletion_has_a_grace_period_and_can_be_cancelled()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tenant = Tenant.Create("acme", "Acme", Owner, TenantIsolation.Shared);

        tenant.RequestDeletion(now, 30, "closing");
        tenant.Status.Should().Be(TenantStatus.PendingDeletion);
        tenant.PurgeAfter.Should().Be(now.AddDays(30));

        tenant.CancelDeletion();
        tenant.Status.Should().Be(TenantStatus.Suspended, "a platform admin reactivates explicitly after review");
        tenant.PurgeAfter.Should().BeNull();
    }

    [Fact]
    public void Data_purge_can_only_be_requested_after_deletion()
    {
        var tenant = Tenant.Create("acme", "Acme", Owner, TenantIsolation.Shared);
        FluentActions.Invoking(tenant.RequestDataPurge).Should().Throw<InvalidTenantException>();

        tenant.RequestDeletion(DateTime.UtcNow, 0, null);
        tenant.RequestDataPurge();
        tenant.DomainEvents.Should().ContainSingle(e => e is TenantDataPurgeRequestedEvent);
    }

    [Fact]
    public void The_default_tenant_cannot_be_suspended_or_deleted()
    {
        var tenant = Tenant.Create("default", "Default", Guid.Empty, TenantIsolation.Shared, id: TenantDefaults.DefaultTenantId);
        FluentActions.Invoking(() => tenant.Suspend("x")).Should().Throw<InvalidTenantException>();
        FluentActions.Invoking(() => tenant.RequestDeletion(DateTime.UtcNow, 1, null)).Should().Throw<InvalidTenantException>();
    }

    [Fact]
    public void Custom_domains_are_normalized_unique_and_verifiable()
    {
        var tenant = Tenant.Create("acme", "Acme", Owner, TenantIsolation.Shared);
        var domain = tenant.AddDomain("Shop.Acme.COM.");
        domain.Host.Should().Be("shop.acme.com");
        domain.IsVerified.Should().BeFalse();
        domain.VerificationRecordName.Should().Be("_tenant-verify.shop.acme.com");

        FluentActions.Invoking(() => tenant.AddDomain("shop.acme.com")).Should().Throw<InvalidTenantException>();
        FluentActions.Invoking(() => tenant.AddDomain("not a host")).Should().Throw<InvalidTenantException>();

        tenant.VerifyDomain("shop.acme.com", DateTime.UtcNow);
        domain.IsVerified.Should().BeTrue();
    }

    [Fact]
    public void ToInfo_carries_what_the_pipeline_needs()
    {
        var tenant = Tenant.Create("acme", "Acme", Owner, TenantIsolation.Dedicated, "acme-db", "eu");
        var info = tenant.ToInfo();
        info.Should().BeEquivalentTo(new
        {
            tenant.Id,
            Identifier = "acme",
            Isolation = TenantIsolation.Dedicated,
            ConnectionSecretRef = "acme-db",
            Region = "eu"
        });
    }
}

public sealed class BranchTests
{
    private static Branch Root(string code = "HQ") => Branch.Create(code, "Head office", null, 6, isHeadOffice: true);

    [Fact]
    public void Paths_and_depths_follow_the_tree()
    {
        var hq = Root();
        var cairo = Branch.Create("CAI", "Cairo", hq, 6);
        var giza = Branch.Create("GIZ", "Giza", cairo, 6);

        hq.Path.Should().Be($"/{hq.Id:N}/");
        giza.Path.Should().Be($"/{hq.Id:N}/{cairo.Id:N}/{giza.Id:N}/");
        giza.Depth.Should().Be(3);
        hq.IsAncestorOrSelfOf(giza.Path).Should().BeTrue();
        cairo.IsAncestorOrSelfOf(hq.Path).Should().BeFalse();
    }

    [Fact]
    public void Max_depth_is_enforced()
    {
        var hq = Root();
        var level2 = Branch.Create("L2", "L2", hq, 2);
        FluentActions.Invoking(() => Branch.Create("L3", "L3", level2, 2)).Should().Throw<InvalidBranchException>();
    }

    [Fact]
    public void A_branch_cannot_move_under_its_own_subtree()
    {
        var hq = Root();
        var cairo = Branch.Create("CAI", "Cairo", hq, 6);
        var giza = Branch.Create("GIZ", "Giza", cairo, 6);

        FluentActions.Invoking(() => cairo.MoveUnder(giza, 6, 1)).Should().Throw<InvalidBranchException>();
        FluentActions.Invoking(() => cairo.MoveUnder(cairo, 6, 1)).Should().Throw<InvalidBranchException>();
    }

    [Fact]
    public void Moving_returns_the_prefixes_to_rewrite_descendants()
    {
        var hq = Root();
        var alex = Branch.Create("ALX", "Alexandria", hq, 6);
        var cairo = Branch.Create("CAI", "Cairo", hq, 6);
        var oldPath = cairo.Path;

        var (oldPrefix, newPrefix, delta) = cairo.MoveUnder(alex, 6, subtreeHeight: 1);

        oldPrefix.Should().Be(oldPath);
        newPrefix.Should().Be($"{alex.Path}{cairo.Id:N}/");
        delta.Should().Be(1);
        cairo.ParentId.Should().Be(alex.Id);
        cairo.Depth.Should().Be(3);
    }

    [Fact]
    public void Moving_respects_the_depth_of_the_whole_subtree()
    {
        var hq = Root();
        var a = Branch.Create("A", "A", hq, 3);
        var b = Branch.Create("B", "B", hq, 3);
        FluentActions.Invoking(() => b.MoveUnder(a, 3, subtreeHeight: 1)).Should().Throw<InvalidBranchException>();
    }

    [Fact]
    public void Codes_are_normalized_and_validated()
    {
        Branch.NormalizeCode(" cai-01 ").Should().Be("CAI-01");
        FluentActions.Invoking(() => Branch.NormalizeCode("bad code")).Should().Throw<InvalidBranchException>();
    }

    [Fact]
    public void The_head_office_cannot_be_deactivated() =>
        FluentActions.Invoking(Root().Deactivate).Should().Throw<InvalidBranchException>();
}

public sealed class MembershipTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Every_change_rotates_the_stamp_and_notifies()
    {
        var membership = TenantMembership.Create(Tenant, Guid.NewGuid(), [], [], DateTime.UtcNow);
        var stamp = membership.Stamp;
        membership.ClearDomainEvents();

        membership.SetRoles([Guid.NewGuid()]);
        membership.Stamp.Should().NotBe(stamp);
        membership.DomainEvents.Should().ContainSingle(e => e is TenantMembershipChangedEvent);
    }

    [Fact]
    public void Owners_cannot_be_suspended_or_removed()
    {
        var owner = TenantMembership.Create(Tenant, Guid.NewGuid(), [], [], DateTime.UtcNow, isOwner: true);
        FluentActions.Invoking(owner.Suspend).Should().Throw<InvalidMembershipException>();
        FluentActions.Invoking(owner.Remove).Should().Throw<InvalidMembershipException>();
    }

    [Fact]
    public void Branch_assignments_are_deduplicated()
    {
        var branch = Guid.NewGuid();
        var membership = TenantMembership.Create(Tenant, Guid.NewGuid(), [],
            [new BranchAssignment(branch, false), new BranchAssignment(branch, true), new BranchAssignment(Guid.Empty)],
            DateTime.UtcNow);

        membership.Branches.Should().ContainSingle().Which.Should().Be(new BranchAssignment(branch, true));
    }
}

public sealed class InvitationTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Only_the_token_hash_is_stored()
    {
        var (invitation, token) = TenantInvitation.Create(Guid.NewGuid(), "A@Example.com", [], [], Guid.NewGuid(), Now,
            TimeSpan.FromDays(7));

        invitation.Email.Should().Be("a@example.com");
        invitation.TokenHash.Should().Be(TenantInvitation.HashToken(token)).And.NotContain(token);
    }

    [Fact]
    public void Accepting_requires_the_invited_email_and_a_valid_invitation()
    {
        var (invitation, _) = TenantInvitation.Create(Guid.NewGuid(), "a@example.com", [], [], Guid.NewGuid(), Now,
            TimeSpan.FromDays(7));

        FluentActions.Invoking(() => invitation.Accept(Guid.NewGuid(), "b@example.com", Now))
            .Should().Throw<InvalidMembershipException>();

        invitation.Accept(Guid.NewGuid(), "A@example.com", Now);
        invitation.Status.Should().Be(TenantInvitationStatus.Accepted);

        FluentActions.Invoking(() => invitation.Accept(Guid.NewGuid(), "a@example.com", Now))
            .Should().Throw<InvalidMembershipException>("an invitation is single-use");
    }

    [Fact]
    public void Expired_invitations_cannot_be_accepted()
    {
        var (invitation, _) = TenantInvitation.Create(Guid.NewGuid(), "a@example.com", [], [], Guid.NewGuid(), Now,
            TimeSpan.FromDays(1));

        FluentActions.Invoking(() => invitation.Accept(Guid.NewGuid(), "a@example.com", Now.AddDays(2)))
            .Should().Throw<InvalidMembershipException>();
    }
}

public sealed class TenantRoleTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void System_roles_are_read_only_and_use_the_code_definition()
    {
        var admin = TenantRole.CreateSystem(Tenant, SystemTenantRoles.Admin);
        admin.IsSystem.Should().BeTrue();
        admin.EffectivePermissions.Should().BeEquivalentTo(TenantPermissions.All);
        FluentActions.Invoking(() => admin.Update("x", null, [])).Should().Throw<InvalidMembershipException>();
    }

    [Fact]
    public void System_role_ids_are_deterministic_per_tenant()
    {
        TenantRole.CreateSystem(Tenant, SystemTenantRoles.Member).Id
            .Should().Be(SystemTenantRoles.IdOf(Tenant, SystemTenantRoles.Member));
        SystemTenantRoles.IdOf(Tenant, SystemTenantRoles.Member)
            .Should().NotBe(SystemTenantRoles.IdOf(Guid.NewGuid(), SystemTenantRoles.Member));
    }

    [Fact]
    public void Custom_roles_reject_unknown_permissions()
    {
        FluentActions.Invoking(() => TenantRole.CreateCustom(Tenant, "Ops", null, ["made.up"]))
            .Should().Throw<InvalidMembershipException>();

        var role = TenantRole.CreateCustom(Tenant, "Ops", null, [TenantPermissions.BranchesRead, TenantPermissions.BranchesRead]);
        role.Permissions.Should().ContainSingle();
        role.DomainEvents.Should().ContainSingle(e => e is TenantRolesChangedEvent);
    }

    [Fact]
    public void Every_system_role_grants_only_known_permissions() =>
        SystemTenantRoles.All.SelectMany(r => r.Permissions).Should().OnlyContain(p => TenantPermissions.All.Contains(p));
}
