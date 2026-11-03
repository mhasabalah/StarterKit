using Shared.Core.MultiTenancy.Authorization;
using Identity.Authorization;
using Identity.Domains.Roles.Constants;
using Shared.Core.Authorization;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity.Tests.Authorization;

public sealed class RolePermissionMapTests
{
    public static TheoryData<string> AllRoles => new(RoleConstants.GetAllRoles());

    [Fact]
    public void Every_role_has_an_explicit_entry_and_no_unknown_role_is_mapped()
    {
        RolePermissionMap.Roles.Should().BeEquivalentTo(RoleConstants.GetAllRoles());
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void Every_mapped_permission_is_a_known_permission(string role)
    {
        RolePermissionMap.For(role).Except(Permissions.All.Concat(TenantPermissions.All)).Should().BeEmpty();
    }

    [Fact]
    public void SuperAdmin_has_every_permission()
    {
        // Platform permissions + every tenant permission (honoured only in single-tenant mode, see TenantPermissionHandler).
        RolePermissionMap.For(RoleConstants.SuperAdmin).Should().BeEquivalentTo(Permissions.All.Concat(TenantPermissions.All));
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void SuperAdmin_is_a_superset_of_every_role(string role)
    {
        RolePermissionMap.For(role).Except(RolePermissionMap.For(RoleConstants.SuperAdmin)).Should().BeEmpty();
    }

    [Fact]
    public void Admin_has_everything_except_admins_manage()
    {
        RolePermissionMap.For(RoleConstants.Admin).Should()
            .BeEquivalentTo(Permissions.All.Where(p => p != Permissions.AdminsManage).Concat(TenantPermissions.All))
            .And.NotContain(Permissions.AdminsManage);
    }

    [Fact]
    public void Finance_has_billing_plans_payment_settings_and_audit_only()
    {
        RolePermissionMap.For(RoleConstants.Finance).Should().BeEquivalentTo(
        [
            Permissions.BillingRead, Permissions.BillingApprove, Permissions.BillingRefund, Permissions.BillingExport,
            Permissions.PlansManage, Permissions.PaymentSettingsManage, Permissions.AuditRead
        ]);
    }

    [Fact]
    public void Support_is_read_only_billing_and_users_plus_audited_impersonation()
    {
        RolePermissionMap.For(RoleConstants.Support).Should()
            .BeEquivalentTo([Permissions.BillingRead, Permissions.UsersRead, Permissions.TenantsRead,
                Permissions.TenantsImpersonate]);
    }

    [Fact]
    public void User_and_unknown_roles_have_no_permissions()
    {
        RolePermissionMap.For(RoleConstants.User).Should().BeEmpty();
        RolePermissionMap.For("NotARole").Should().BeEmpty();
        RolePermissionMap.For(["NotARole", RoleConstants.User]).Should().BeEmpty();
    }

    [Fact]
    public void Union_of_roles_is_deduplicated_and_sorted()
    {
        var permissions = RolePermissionMap.For(
            [RoleConstants.Finance, RoleConstants.Support, RoleConstants.Support, RoleConstants.User]);

        permissions.Should().OnlyHaveUniqueItems();
        permissions.Should().BeInAscendingOrder(StringComparer.Ordinal);
        permissions.Should().Contain([Permissions.BillingRead, Permissions.UsersRead, Permissions.AuditRead]);
        permissions.Count(p => p == Permissions.BillingRead).Should().Be(1);
    }

    [Fact]
    public void Every_permission_has_a_unique_stable_policy_name()
    {
        Permissions.All.Should().OnlyHaveUniqueItems();
        Permissions.All.Select(Permissions.PolicyName).Should().OnlyContain(n => n.StartsWith("perm:"));
    }
}
