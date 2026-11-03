using Microsoft.AspNetCore.Builder;

namespace Shared.Core.Authorization;

/// <summary>
/// Fine-grained permissions for admin features (shared contract across modules).
/// <list type="bullet">
/// <item>Roles map to permissions in <see cref="RolePermissionMap"/>. Tokens carry roles only;
/// <see cref="PermissionClaimsTransformation"/> adds the <see cref="ClaimType"/> claims on every request.</item>
/// <item>Each permission has an authorization policy named <see cref="PolicyName"/>, registered by the Identity
/// module. Endpoints opt in with <see cref="PermissionEndpointExtensions.RequirePermission{TBuilder}"/>.</item>
/// <item>Add a new permission here, map it to roles in <see cref="RolePermissionMap"/>, then use it on endpoints.</item>
/// </list>
/// Values are stable strings, because they're stored in tokens and maybe in client code. Never rename one;
/// add a new value and remove the old one later.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "permission";
    public const string PolicyPrefix = "perm:";

    // Billing / subscriptions
    public const string BillingRead = "billing.read";
    public const string BillingApprove = "billing.approve";
    public const string BillingRefund = "billing.refund";
    public const string BillingExport = "billing.export";
    public const string PlansManage = "plans.manage";
    public const string PaymentSettingsManage = "payment-settings.manage";

    // Users / admin
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string AdminsManage = "admins.manage";

    // Governance
    public const string AuditRead = "audit.read";

    // Catalog moderation (sample module)
    public const string CatalogModerate = "catalog.moderate";

    // Multi-tenancy (platform/host administration of tenants)
    public const string TenantsRead = "tenants.read";
    public const string TenantsManage = "tenants.manage";

    /// <summary>Support impersonation: act as a tenant member for a short, audited, read-mostly session.</summary>
    public const string TenantsImpersonate = "tenants.impersonate";

    /// <summary>Every permission; used to register one policy per permission.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        BillingRead, BillingApprove, BillingRefund, BillingExport, PlansManage, PaymentSettingsManage,
        UsersRead, UsersManage, AdminsManage, AuditRead, CatalogModerate, TenantsRead, TenantsManage,
        TenantsImpersonate
    ];

    public static string PolicyName(string permission) => PolicyPrefix + permission;
}

public static class PermissionEndpointExtensions
{
    /// <summary>
    /// Requires the caller to hold <paramref name="permission"/>. The policy itself is registered by the Identity
    /// module. Admin 2FA, when enforced, is part of that policy.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Permissions.PolicyName(permission));
}
