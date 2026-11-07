using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Contracts.Mediation;
using Shared.Core.Authorization;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Web;
using Tenancy.Domains.Roles.Models;
using Tenancy.Services;
using UserDirectory.Contracts.Queries;

namespace Tenancy.Tests;

/// <summary>
/// Single-tenant mode has no memberships: a platform Admin/SuperAdmin acts through their platform roles. In a
/// background job (no HTTP request, no token claims) those roles come from the user directory for the acting user.
/// </summary>
public sealed class TenancyRequestActingRolesTests
{
    private static TenancyRequest Request(bool multiTenant = false)
    {
        var tenant = new Mock<ITenantContext>();
        var options = Options.Create(new MultiTenancyOptions { Enabled = multiTenant });
        // The REAL service: without an HttpContext it answers with the ActingUser and no roles.
        return new TenancyRequest(tenant.Object, new HttpContextService(new HttpContextAccessor()), options);
    }

    private static ISender Directory(Guid userId, params string[] roles)
    {
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.Is<GetUserRolesQuery>(q => q.UserId == userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)roles);
        return sender.Object;
    }

    [Fact]
    public async Task Without_loading_the_roles_a_job_requester_holds_nothing_in_single_tenant_mode()
    {
        var userId = Guid.NewGuid();
        using var acting = ActingUser.Begin(userId);
        var request = Request();

        request.IsOwner.Should().BeFalse();
        request.Permissions.Should().BeEmpty();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_super_admin_acting_in_a_job_is_the_owner_and_may_grant_everything()
    {
        var userId = Guid.NewGuid();
        using var acting = ActingUser.Begin(userId);
        var request = Request();

        await request.LoadActingRolesAsync(Directory(userId, RoleConstants.SuperAdmin), default);

        request.IsPlatformAdmin.Should().BeTrue();
        request.IsOwner.Should().BeTrue();
        request.Permissions.Should().Contain(TenantPermissions.All);
        FluentActions.Invoking(() => request.EnsureCanGrant(TenantPermissions.All)).Should().NotThrow();
    }

    [Fact]
    public async Task A_plain_user_acting_in_a_job_still_cannot_grant_in_single_tenant_mode()
    {
        var userId = Guid.NewGuid();
        using var acting = ActingUser.Begin(userId);
        var request = Request();

        await request.LoadActingRolesAsync(Directory(userId, RoleConstants.User), default);

        request.IsOwner.Should().BeFalse();
        FluentActions.Invoking(() => request.EnsureCanGrant([TenantPermissions.MembersManage]))
            .Should().Throw<ForbiddenException>();
    }

    [Fact]
    public async Task Loading_is_a_no_op_without_an_acting_user()
    {
        var sender = new Mock<ISender>();

        await Request().LoadActingRolesAsync(sender.Object, default);

        sender.Verify(s => s.Send(It.IsAny<GetUserRolesQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

/// <summary>The system role names are part of the member import/export file contract (role names in the cell).</summary>
public sealed class SystemTenantRolesContractTests
{
    [Fact]
    public void System_role_names_and_grants_are_pinned_because_import_files_refer_to_them()
    {
        SystemTenantRoles.Admin.Name.Should().Be("Administrator");
        SystemTenantRoles.BranchManager.Name.Should().Be("Branch manager");
        SystemTenantRoles.Member.Name.Should().Be("Member");
        SystemTenantRoles.Admin.Permissions.Should().BeEquivalentTo(TenantPermissions.All);
        SystemTenantRoles.All.Select(r => r.Name).Should().NotContain(n => n.Contains(';'));
    }

    [Fact]
    public void A_role_name_with_a_semicolon_is_refused()
    {
        FluentActions.Invoking(() => TenantRole.CreateCustom(Guid.NewGuid(), "A;B", null, []))
            .Should().Throw<Tenancy.Domains.Tenants.Exceptions.InvalidMembershipException>();
        TenantRole.IsListSafeName("Plain name").Should().BeTrue();
    }
}
