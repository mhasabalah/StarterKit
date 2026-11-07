using DataTransfer.Contracts.Definitions;
using DataTransfer.Contracts.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;
using Tenancy.Contracts.Branches;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Roles;
using Tenancy.Contracts.Tenants;
using Tenancy.Domains.Invitations.Models;
using Tenancy.Domains.Members.DataTransfer;
using Tenancy.Domains.Roles.Models;

namespace Tenancy.IntegrationTests;

/// <summary>
/// The tenancy.members import/export definitions on real Postgres, called the way the DataTransfer job calls them:
/// inside the requester's tenant scope + membership and an <see cref="ActingUser"/> block, with NO HTTP user (the
/// fixture's HTTP service is switched to the real one, which falls back to the acting user).
/// </summary>
[Collection(TenancyPostgresCollection.Name)]
public sealed class MemberTransferDefinitionTests(TenancyPostgresFixture fx) : IAsyncLifetime
{
    private Guid _tenantId;
    private Guid _ownerId;
    private TenantInfo _tenant = default!;

    public async Task InitializeAsync()
    {
        _ownerId = Guid.NewGuid();
        var identifier = $"m-{Guid.NewGuid():N}"[..20];
        var created = await fx.SendAs(_ownerId, new CreateTenantCommand("Members Co", identifier));
        _tenantId = created.TenantId;
        _tenant = new TenantInfo(_tenantId, identifier, "Members Co");
        fx.PlanLimits.MaxUsers = null;
        fx.Users.Add(_ownerId, $"owner-{_ownerId:N}@example.com", "Owner Person");
    }

    public Task DisposeAsync()
    {
        fx.PlanLimits.MaxUsers = null;
        fx.Http.Real = null;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Rows_invite_by_email_with_role_names_and_branch_codes_acting_as_the_requester()
    {
        var branch = await CreateBranchAsync("CAIRO");
        var email = Email();

        var results = await ApplyAsync(_ownerId, false,
            Row(2, email, roles: "Member; branch manager", branches: "cairo"));

        results.Single().Outcome.Should().Be(ImportRowOutcome.Created);
        var invitation = await fx.WithDb(db => db.Invitations.SingleAsync(i => i.TenantId == _tenantId && i.Email == email));
        invitation.InvitedBy.Should().Be(_ownerId, "the job acts as the requester through ActingUser, not as the system");
        invitation.Status.Should().Be(TenantInvitationStatus.Pending);
        invitation.RoleIds.Should().BeEquivalentTo(
            [SystemTenantRoles.IdOf(_tenantId, SystemTenantRoles.Member), SystemTenantRoles.IdOf(_tenantId, SystemTenantRoles.BranchManager)]);
        invitation.Branches.Should().ContainSingle().Which.BranchId.Should().Be(branch);
    }

    [Fact]
    public async Task A_second_run_and_a_repeat_in_the_same_file_leave_the_pending_invitation_alone()
    {
        var email = Email();

        var first = await ApplyAsync(_ownerId, false, Row(2, email), Row(3, email.ToUpperInvariant()));
        var again = await ApplyAsync(_ownerId, false, Row(2, email));

        first.Select(r => r.Outcome).Should().Equal(ImportRowOutcome.Created, ImportRowOutcome.Unchanged);
        again.Single().Outcome.Should().Be(ImportRowOutcome.Unchanged);
        (await fx.WithDb(db => db.Invitations.CountAsync(i => i.TenantId == _tenantId && i.Email == email))).Should().Be(1);
    }

    [Fact]
    public async Task An_existing_member_is_unchanged_and_never_invited()
    {
        var userId = Guid.NewGuid();
        var email = Email();
        fx.Users.Add(userId, email, "Existing Person");
        await fx.SendInTenant(_tenantId, _ownerId, new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim));

        var results = await ApplyAsync(_ownerId, false, Row(2, email.ToUpperInvariant()));

        results.Single().Outcome.Should().Be(ImportRowOutcome.Unchanged);
        (await fx.WithDb(db => db.Invitations.AnyAsync(i => i.TenantId == _tenantId && i.Email == email))).Should().BeFalse();
    }

    [Fact]
    public async Task Bad_rows_fail_alone_with_a_column_and_the_good_row_is_still_invited()
    {
        var good = Email();

        var results = await ApplyAsync(_ownerId, false,
            Row(2, good),
            Row(3, Email(), roles: "No Such Role"),
            Row(4, Email(), branches: "NOPE"),
            Row(5, "not-an-email"),
            Row(6, null));

        results[0].Outcome.Should().Be(ImportRowOutcome.Created);
        results[1].Errors.Single().Should().Match<ImportRowError>(e => e.Column == "roles" && e.Code == "role_not_found");
        results[2].Errors.Single().Should().Match<ImportRowError>(e => e.Column == "branches" && e.Code == "branch_not_found");
        results[3].Errors.Single().Column.Should().Be("email");
        results[4].Errors.Single().Should().Match<ImportRowError>(e => e.Column == "email" && e.Code == "required");
        (await fx.WithDb(db => db.Invitations.CountAsync(i => i.TenantId == _tenantId))).Should().Be(1);
    }

    [Fact]
    public async Task A_dry_run_validates_and_reports_but_invites_nobody()
    {
        var email = Email();

        var results = await ApplyAsync(_ownerId, true, Row(2, email), Row(3, Email(), roles: "Ghost"));

        results[0].Outcome.Should().Be(ImportRowOutcome.Created);
        results[1].Errors.Single().Code.Should().Be("role_not_found");
        (await fx.WithDb(db => db.Invitations.CountAsync(i => i.TenantId == _tenantId))).Should().Be(0);
    }

    [Fact]
    public async Task The_plan_seat_limit_fails_the_row_with_tenant_limit_exceeded()
    {
        fx.PlanLimits.MaxUsers = 1; // the owner already takes the only seat

        var results = await ApplyAsync(_ownerId, false, Row(2, Email()));

        results.Single().Errors.Single().Code.Should().Be("tenant_limit_exceeded");
        (await fx.WithDb(db => db.Invitations.CountAsync(i => i.TenantId == _tenantId))).Should().Be(0);
    }

    [Fact]
    public async Task A_requester_cannot_grant_a_role_beyond_their_own_permissions()
    {
        var manager = Guid.NewGuid();
        await fx.SendInTenant(_tenantId, _ownerId, new ProvisionMemberCommand(manager, MemberProvisioningSource.Scim,
            [SystemTenantRoles.IdOf(_tenantId, SystemTenantRoles.BranchManager)]));

        var results = await ApplyAsync(manager, false, Row(2, Email(), roles: "Administrator"));

        results.Single().Errors.Single().Code.Should().Be("forbidden");
    }

    [Fact]
    public async Task Export_lists_members_with_role_names_and_branch_codes_and_reimports_unchanged()
    {
        await CreateBranchAsync("ALEX");
        var userId = Guid.NewGuid();
        var email = Email();
        fx.Users.Add(userId, email, "Alex Person");
        await fx.SendInTenant(_tenantId, _ownerId, new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim,
            [SystemTenantRoles.IdOf(_tenantId, SystemTenantRoles.BranchManager)]));
        var branchId = await fx.WithDb(db => db.Branches.Where(b => b.TenantId == _tenantId && b.Code == "ALEX").Select(b => b.Id).SingleAsync());
        await fx.WithDb(async db =>
        {
            var membership = await db.Memberships.SingleAsync(m => m.TenantId == _tenantId && m.UserId == userId);
            membership.SetBranches([new BranchAssignment(branchId, false)]);
            return await db.SaveChangesAsync();
        });

        var exported = await ExportAsync();

        exported.Should().HaveCount(2, "the owner and the provisioned member");
        var row = exported.Single(r => r[0] == email);
        row[1].Should().Be("Branch manager");
        row[2].Should().Be("ALEX:self");
        row[3].Should().Be("Alex Person");
        row[4].Should().Be(nameof(TenantMembershipStatus.Active));
        row[5].Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$");

        var names = MemberTransferColumns.Export.Select(c => c.Name).ToList();
        var again = await ApplyAsync(_ownerId, false, exported
            .Select((cells, i) => new ImportRecord(i + 2, names.Select((n, j) => (n, cells[j])).ToDictionary(x => x.n, x => x.Item2)))
            .ToArray());
        again.Select(r => r.Outcome).Should().OnlyContain(o => o == ImportRowOutcome.Unchanged);
    }

    [Fact]
    public async Task A_branch_restricted_requester_cannot_assign_other_branches_and_only_that_row_fails()
    {
        var mine = await CreateBranchAsync("MINE");
        await CreateBranchAsync("OTHER");
        var manager = await ProvisionManagerAsync(new BranchAssignment(mine, false));

        var results = await ApplyAsync(manager, false,
            Row(2, Email(), branches: "MINE"),
            Row(3, Email(), branches: "OTHER"),
            Row(4, Email()));

        results[0].Outcome.Should().Be(ImportRowOutcome.Created);
        results[1].Errors.Single().Should().Match<ImportRowError>(e =>
            e.Column == "branches" && (e.Code == "branch_not_allowed" || e.Code == "branch_not_found"));
        results[2].Outcome.Should().Be(ImportRowOutcome.Created);
        (await fx.WithDb(db => db.Invitations.CountAsync(i => i.TenantId == _tenantId))).Should().Be(2);
    }

    [Fact]
    public async Task A_branch_outside_the_requesters_scope_is_reported_as_branch_not_allowed_in_a_dry_run_too()
    {
        var mine = await CreateBranchAsync("MINE2");
        await CreateBranchAsync("OTHER2");
        var manager = await ProvisionManagerAsync(new BranchAssignment(mine, false));

        var dry = await ApplyAsync(manager, true, Row(2, Email(), branches: "OTHER2"));
        var real = await ApplyAsync(manager, false, Row(2, Email(), branches: "OTHER2"));

        dry.Single().Errors.Single().Column.Should().Be("branches");
        real.Single().Errors.Single().Code.Should().Be(dry.Single().Errors.Single().Code);
        (await fx.WithDb(db => db.Invitations.CountAsync(i => i.TenantId == _tenantId))).Should().Be(0);
    }

    [Fact]
    public async Task An_escalating_role_writes_no_invitation_in_either_mode()
    {
        var manager = await ProvisionManagerAsync();

        var real = await ApplyAsync(manager, false, Row(2, Email(), roles: "Administrator"));
        var dry = await ApplyAsync(manager, true, Row(2, Email(), roles: "Administrator"));

        real.Single().Errors.Single().Code.Should().Be("forbidden");
        dry.Single().Errors.Single().Code.Should().Be("forbidden");
        (await fx.WithDb(db => db.Invitations.CountAsync(i => i.TenantId == _tenantId))).Should().Be(0);
    }

    [Fact]
    public async Task A_dry_run_queues_no_notification_and_a_real_run_goes_through_the_sender()
    {
        fx.Notifications.Invocations.Clear();

        await ApplyAsync(_ownerId, true, Row(2, Email()));

        fx.Notifications.Verify(n => n.ToEmail(It.IsAny<string>(), It.IsAny<Notification.Contracts.Enums.NotificationCulture>(),
            It.IsAny<Guid?>()), Times.Never);

        await ApplyAsync(_ownerId, false, Row(2, Email()));

        fx.Notifications.Verify(n => n.ToEmail(It.IsAny<string>(), It.IsAny<Notification.Contracts.Enums.NotificationCulture>(),
            It.IsAny<Guid?>()), Times.Once);
    }

    [Fact]
    public async Task A_pending_invitation_of_another_tenant_does_not_make_the_row_unchanged()
    {
        var email = Email();
        var otherOwner = Guid.NewGuid();
        var other = await fx.SendAs(otherOwner, new CreateTenantCommand("Other Co", $"o-{Guid.NewGuid():N}"[..20]));
        await fx.SendInTenant(other.TenantId, otherOwner, new InviteMemberCommand(email, [], []));

        var results = await ApplyAsync(_ownerId, false, Row(2, email));

        results.Single().Outcome.Should().Be(ImportRowOutcome.Created);
        (await fx.WithDb(db => db.Invitations.CountAsync(i => i.Email == email))).Should().Be(2);
    }

    [Fact]
    public async Task A_member_of_another_tenant_is_still_invited_here()
    {
        var userId = Guid.NewGuid();
        var email = Email();
        fx.Users.Add(userId, email, "Elsewhere Person");
        var otherOwner = Guid.NewGuid();
        var other = await fx.SendAs(otherOwner, new CreateTenantCommand("Other Co", $"o-{Guid.NewGuid():N}"[..20]));
        await fx.SendInTenant(other.TenantId, otherOwner, new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim));

        var results = await ApplyAsync(_ownerId, false, Row(2, email));

        results.Single().Outcome.Should().Be(ImportRowOutcome.Created);
    }

    [Fact]
    public async Task A_suspended_member_is_unchanged_not_invited()
    {
        var userId = Guid.NewGuid();
        var email = Email();
        fx.Users.Add(userId, email, "Suspended Person");
        await fx.SendInTenant(_tenantId, _ownerId, new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim));
        await fx.SendInTenant(_tenantId, _ownerId, new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim, Active: false));

        var results = await ApplyAsync(_ownerId, false, Row(2, email));

        results.Single().Outcome.Should().Be(ImportRowOutcome.Unchanged);
    }

    [Fact]
    public async Task A_custom_role_is_assignable_by_name_and_an_over_long_email_is_a_row_error()
    {
        var role = TenantRole.CreateCustom(_tenantId, "Auditor", null, [TenantPermissions.AuditRead]);
        await fx.WithDb(async db =>
        {
            db.Roles.Add(role);
            return await db.SaveChangesAsync();
        });
        var email = Email();

        var results = await ApplyAsync(_ownerId, false,
            Row(2, email, roles: "auditor"),
            Row(3, new string('a', 250) + "@example.com"));

        results[0].Outcome.Should().Be(ImportRowOutcome.Created);
        (await fx.WithDb(db => db.Invitations.SingleAsync(i => i.Email == email))).RoleIds.Should().Equal(role.Id);
        results[1].Errors.Single().Column.Should().Be("email");
    }

    [Fact]
    public async Task Row_messages_come_out_in_arabic_for_an_arabic_requester()
    {
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ar");
        try
        {
            var results = await ApplyAsync(_ownerId, false, Row(2, Email(), roles: "Ghost"), Row(3, "bad-email"), Row(4, null));

            results[0].Errors.Single().Message.Should().Contain("لا يوجد دور");
            results[1].Errors.Single().Message.Should().Contain("ليس بريدًا");
            results[2].Errors.Single().Message.Should().Contain("مطلوبة");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task Export_leaves_out_inactive_branches_so_the_file_always_reimports()
    {
        var active = await CreateBranchAsync("LIVE");
        var closed = await CreateBranchAsync("CLOSED");
        var userId = Guid.NewGuid();
        var email = Email();
        fx.Users.Add(userId, email, "Two Branch Person");
        await fx.SendInTenant(_tenantId, _ownerId, new ProvisionMemberCommand(userId, MemberProvisioningSource.Scim));
        await fx.WithDb(async db =>
        {
            var membership = await db.Memberships.SingleAsync(m => m.TenantId == _tenantId && m.UserId == userId);
            membership.SetBranches([new BranchAssignment(active, true), new BranchAssignment(closed, true)]);
            (await db.Branches.SingleAsync(b => b.Id == closed)).Deactivate();
            return await db.SaveChangesAsync();
        });

        var exported = await ExportAsync();

        exported.Single(r => r[0] == email)[2].Should().Be("LIVE");
    }

    [Fact]
    public async Task Role_names_with_a_semicolon_are_refused_because_they_would_break_the_roles_cell()
    {
        var act = () => TenantRole.CreateCustom(_tenantId, "Ops;Admin", null, []);
        act.Should().Throw<Tenancy.Domains.Tenants.Exceptions.InvalidMembershipException>();
        new Tenancy.Domains.Roles.Features.CreateTenantRoleCommandValidator()
            .Validate(new CreateTenantRoleCommand("Ops;Admin", null, [])).IsValid.Should().BeFalse();
        await Task.CompletedTask;
    }

    private async Task<Guid> ProvisionManagerAsync(params BranchAssignment[] branches)
    {
        var manager = Guid.NewGuid();
        await fx.SendInTenant(_tenantId, _ownerId, new ProvisionMemberCommand(manager, MemberProvisioningSource.Scim,
            [SystemTenantRoles.IdOf(_tenantId, SystemTenantRoles.BranchManager)]));
        if (branches.Length > 0)
        {
            await fx.WithDb(async db =>
            {
                var membership = await db.Memberships.SingleAsync(m => m.TenantId == _tenantId && m.UserId == manager);
                membership.SetBranches(branches);
                return await db.SaveChangesAsync();
            });
        }

        return manager;
    }

    private async Task<Guid> CreateBranchAsync(string code) =>
        (await fx.SendInTenant(_tenantId, _ownerId, new CreateBranchCommand(null, code, code))).Id;

    private static string Email() => $"invitee-{Guid.NewGuid():N}@example.com";

    private async Task<IReadOnlyList<ImportRowResult>> ApplyAsync(Guid requester, bool dryRun, params ImportRecord[] records)
    {
        IReadOnlyList<ImportRowResult> results = [];
        fx.Http.Real = new HttpContextService(new HttpContextAccessor()); // no HTTP request, like a Hangfire job
        try
        {
            await fx.WithServices(async sp =>
            {
                TenantMembershipInfo? membership;
                using (TenantScope.BeginHostScope())
                {
                    membership = await sp.GetRequiredService<ITenantMembershipResolver>()
                        .GetMembershipAsync(requester, _tenantId, CancellationToken.None);
                }

                // What TransferJobRunner does around the definition.
                using var scope = TenantScope.Begin(_tenant, membership: membership);
                using var acting = ActingUser.Begin(requester);
                var definition = sp.GetServices<IImportDefinition>().Single(d => d.Key == MemberTransferColumns.Key);
                results = await definition.ApplyBatchAsync(records,
                    new ImportContext(Guid.NewGuid(), requester, dryRun, "en"), CancellationToken.None);
            });
        }
        finally
        {
            fx.Http.Real = null;
        }
        return results;
    }

    private async Task<List<IReadOnlyList<string?>>> ExportAsync()
    {
        var rows = new List<IReadOnlyList<string?>>();
        fx.Http.Real = new HttpContextService(new HttpContextAccessor()); // no HTTP request, like a Hangfire job
        try
        {
            await fx.WithServices(async sp =>
            {
                TenantMembershipInfo? membership;
                using (TenantScope.BeginHostScope())
                {
                    membership = await sp.GetRequiredService<ITenantMembershipResolver>()
                        .GetMembershipAsync(_ownerId, _tenantId, CancellationToken.None);
                }

                using var scope = TenantScope.Begin(_tenant, membership: membership);
                using var acting = ActingUser.Begin(_ownerId);
                var export = sp.GetServices<IExportDefinition>().Single(d => d.Key == MemberTransferColumns.Key);
                await foreach (var row in export.StreamAsync(
                                   new ExportContext(Guid.NewGuid(), _ownerId, new Dictionary<string, string>(), "en"),
                                   CancellationToken.None))
                    rows.Add(row);
            });
        }
        finally
        {
            fx.Http.Real = null;
        }
        return rows;
    }

    private static ImportRecord Row(long row, string? email, string? roles = null, string? branches = null) =>
        new(row, new Dictionary<string, string?>
        {
            [MemberTransferColumns.Email] = email,
            [MemberTransferColumns.Roles] = roles,
            [MemberTransferColumns.Branches] = branches
        });
}
