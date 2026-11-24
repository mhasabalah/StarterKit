using Shared.Core.MultiTenancy.Authorization;

namespace Shared.Core.Authorization;

/// <summary>
/// THE single, code-defined source of "which role may do what". The permissions of a user are the union of the
/// permissions of their roles. It is APPLICATION policy, not identity-provider policy: tokens only carry role
/// claims, and <see cref="PermissionClaimsTransformation"/> expands them into <see cref="Permissions.ClaimType"/>
/// claims on every request - so any identity provider (the Identity module, Keycloak, ...) works unchanged and a
/// change to this map applies to every live token immediately.
/// <para>
/// To add a permission: add the constant to <see cref="Permissions"/> (and to <see cref="Permissions.All"/>),
/// grant it to roles below, then protect endpoints with <c>.RequirePermission(Permissions.X)</c>.
/// </para>
/// </summary>
public static class RolePermissionMap
{
    private static readonly string[] BillingAll =
    [
        Permissions.BillingRead, Permissions.BillingApprove, Permissions.BillingRefund, Permissions.BillingExport
    ];

    private static readonly IReadOnlySet<string> None = new HashSet<string>();

    /// <summary>Platform permissions only SuperAdmin holds (Admin gets every other one).</summary>
    public static IReadOnlySet<string> SuperAdminOnly { get; } = Set(
    [
        Permissions.AdminsManage, Permissions.OutboxManage, Permissions.FeatureFlagsManage,
#if (SEARCH)
        Permissions.SearchOperate
#endif
    ]);

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            // Platform admins also hold every TENANT permission as a platform claim. Those claims are honoured ONLY in
            // single-tenant mode (they manage the Default tenant without memberships); in multi-tenant mode tenant
            // permissions come exclusively from memberships (see TenantPermissionHandler).
            [RoleConstants.SuperAdmin] = Set(Permissions.All.Concat(TenantPermissions.All)),
            [RoleConstants.Admin] = Set(Permissions.All.Where(p => !SuperAdminOnly.Contains(p)).Concat(TenantPermissions.All)),
            [RoleConstants.Finance] = Set(BillingAll.Concat(
            [
                Permissions.PlansManage, Permissions.PaymentSettingsManage, Permissions.AuditRead,
                Permissions.BillingCouponsManage
            ])),
            [RoleConstants.Support] = Set([Permissions.BillingRead, Permissions.UsersRead, Permissions.TenantsRead,
                Permissions.TenantsImpersonate, Permissions.SessionsManage]),
            [RoleConstants.User] = None
        };

    /// <summary>Roles that have an entry (every seeded role; enforced by tests).</summary>
    public static IEnumerable<string> Roles => Map.Keys;

    /// <summary>Permissions of one role; empty for unknown roles.</summary>
    public static IReadOnlySet<string> For(string role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : None;

    /// <summary>De-duplicated, ordinal-sorted union of the permissions of <paramref name="roles"/>.</summary>
    public static IReadOnlyList<string> For(IEnumerable<string> roles) =>
        roles.SelectMany(For).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    private static HashSet<string> Set(IEnumerable<string> permissions) => new(permissions, StringComparer.Ordinal);
}
