using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;

namespace Shared.Core.MultiTenancy.Authorization;

/// <summary>
/// Permissions INSIDE a tenant (granted by tenant roles on a membership). Distinct from the platform
/// <see cref="Permissions"/> held by host staff. Values are stable strings - never rename, add and retire.
/// </summary>
public static class TenantPermissions
{
    public const string PolicyPrefix = "tperm:";

    /// <summary>
    /// Pseudo-permission: "is an active member of the current tenant" (any role). Not grantable; policy
    /// <c>tperm:tenant.member</c>, endpoints use <see cref="TenantPermissionEndpointExtensions.RequireTenantMember{TBuilder}"/>.
    /// </summary>
    public const string Member = "tenant.member";

    public const string SettingsManage = "tenant.settings.manage";
    public const string MembersRead = "tenant.members.read";
    public const string MembersManage = "tenant.members.manage";
    public const string RolesManage = "tenant.roles.manage";
    public const string BranchesRead = "tenant.branches.read";
    public const string BranchesManage = "tenant.branches.manage";

    /// <summary>Unrestricted branch scope (sees every branch's data).</summary>
    public const string BranchesAll = "tenant.branches.all";

    public const string AuditRead = "tenant.audit.read";
    public const string BillingManage = "tenant.billing.manage";

    // Business modules (sample)
    public const string ProductsManage = "catalog.products.manage";

    /// <summary>Category CRUD and bulk import/export (Catalog module). Owner and Administrator hold it by default.</summary>
    public const string CategoriesManage = "catalog.categories.manage";

    /// <summary>Outbound webhooks: endpoints, secrets, deliveries (Webhooks module).</summary>
    public const string WebhooksManage = "webhooks.manage";

    /// <summary>Enterprise single sign-on (SAML/OIDC connections, verified email domains) and SCIM directories.</summary>
    public const string SsoManage = "tenant.sso.manage";

    /// <summary>
    /// Bulk CSV/Excel import (DataTransfer module). Each definition also requires its own permission (e.g.
    /// <see cref="ProductsManage"/> for products).
    /// </summary>
    public const string DataImport = "datatransfer.import";

    /// <summary>Bulk CSV/Excel export (DataTransfer module), plus the definition's own permission.</summary>
    public const string DataExport = "datatransfer.export";

    /// <summary>
    /// Start AI agent runs (Agentic module). The agent never exceeds its initiator: every tool call is checked against
    /// the initiator's CURRENT permissions, and each tool also requires its own permission.
    /// </summary>
    public const string AgentRunsStart = "agentic.runs.start";

    /// <summary>Read every agent run of the tenant (without it, members see only the runs they started).</summary>
    public const string AgentRunsRead = "agentic.runs.read";

    /// <summary>
    /// Approve or reject actions an agent proposed. The approver must ALSO hold the proposed tool's own permission:
    /// nobody approves what they could not do themselves.
    /// </summary>
    public const string AgentApprovalsDecide = "agentic.approvals.decide";

    /// <summary>Manage the tenant's agent runs: cancel a run someone else started.</summary>
    public const string AgentsManage = "agentic.manage";

    public static IReadOnlyList<string> All { get; } =
    [
        SettingsManage, MembersRead, MembersManage, RolesManage, BranchesRead, BranchesManage, BranchesAll,
        AuditRead, BillingManage, ProductsManage, CategoriesManage, WebhooksManage, SsoManage, DataImport, DataExport,
        AgentRunsStart, AgentRunsRead, AgentApprovalsDecide, AgentsManage
    ];

    /// <summary>Policies to register: every grantable permission + <see cref="Member"/>.</summary>
    public static IEnumerable<string> Policies => All.Append(Member);

    public static string PolicyName(string permission) => PolicyPrefix + permission;
}

public static class TenantPermissionEndpointExtensions
{
    /// <summary>Requires <paramref name="permission"/> in the CURRENT tenant (policy <c>tperm:&lt;permission&gt;</c>).</summary>
    public static TBuilder RequireTenantPermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(TenantPermissions.PolicyName(permission));

    /// <summary>Requires an active membership in the current tenant (single-tenant mode: any authenticated user).</summary>
    public static TBuilder RequireTenantMember<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(TenantPermissions.PolicyName(TenantPermissions.Member));
}

public sealed class TenantPermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Grants a <see cref="TenantPermissionRequirement"/> when:
/// <list type="bullet">
/// <item>the request's membership (resolved by the tenant middleware) holds the permission (or is the owner), or</item>
/// <item>single-tenant mode only: the caller has the same value as a platform <c>permission</c> claim
/// (so Admin/SuperAdmin manage the Default tenant without memberships).</item>
/// </list>
/// In multi-tenant mode platform claims NEVER grant tenant permissions (impersonation issues a membership instead).
/// </summary>
public sealed class TenantPermissionHandler(ITenantContext tenantContext, IOptions<MultiTenancyOptions> options)
    : AuthorizationHandler<TenantPermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        TenantPermissionRequirement requirement)
    {
        var membership = tenantContext.Membership;
        var isMemberCheck = requirement.Permission == TenantPermissions.Member;

        if (membership is not null && membership.TenantId == tenantContext.TenantId)
        {
            // API keys: membership permissions AND key scopes (a membership check needs no scope).
            if (isMemberCheck ||
                (membership.HasPermission(requirement.Permission) &&
                 ApiKeyPrincipal.Allows(context.User, requirement.Permission)))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }

        if (!options.Value.Enabled &&
            (isMemberCheck
                ? context.User.Identity?.IsAuthenticated == true
                : context.User.HasClaim(Permissions.ClaimType, requirement.Permission)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
