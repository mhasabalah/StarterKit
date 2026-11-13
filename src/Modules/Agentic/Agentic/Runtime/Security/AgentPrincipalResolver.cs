using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy.Authorization;
using UserDirectory.Contracts.Queries;

namespace Agentic.Runtime.Security;

/// <summary>
/// Who a run acts for, as of NOW: the initiating user with their current membership and effective tenant permissions.
/// </summary>
/// <param name="Membership">Multi-tenant mode: the membership (it also carries the branch scope). Null otherwise.</param>
/// <param name="Permissions">Current tenant permissions, already narrowed by the run's permission ceiling.</param>
public sealed record AgentPrincipal(Guid UserId, TenantMembershipInfo? Membership, IReadOnlySet<string> Permissions);

/// <summary>
/// Rebuilds the run's authorization from trusted sources every time it is needed: at the start of each attempt and
/// before each tool call. There is no HTTP request and no token in a job, so nothing is taken from claims; and nothing
/// is cached on the run, so access that was removed while a run waited (in the queue or for an approval) is honoured.
/// <list type="bullet">
/// <item>The user must still exist and be active (user directory).</item>
/// <item>Multi-tenant: they must still be an active member of the run's tenant; permissions come from their roles
/// there (the owner holds all).</item>
/// <item>Single-tenant: permissions come from their platform roles, exactly as their own requests get them.</item>
/// <item>A run started with an API key keeps the key's scopes as a ceiling.</item>
/// </list>
/// Known limit: a temporary sign-in lockout is not visible outside Identity, so it does not stop a run; deactivating
/// the user or removing the membership does.
/// </summary>
public sealed class AgentPrincipalResolver(
    ISender sender,
    ITenantContext tenantContext,
    ITenantMembershipResolver memberships,
    IOptions<MultiTenancyOptions> multiTenancy)
{
    /// <summary>Null when the user may no longer act in this tenant at all.</summary>
    public async Task<AgentPrincipal?> ResolveAsync(Guid userId, IReadOnlyCollection<string>? permissionCeiling,
        CancellationToken cancellationToken)
    {
        var profile = await sender.Send(new GetUserProfileQuery(userId), cancellationToken);
        if (profile is not { IsActive: true })
            return null;

        TenantMembershipInfo? membership = null;
        IEnumerable<string> permissions;
        if (multiTenancy.Value.Enabled)
        {
            if (tenantContext.TenantId is not { } tenantId)
                return null;
            membership = await memberships.GetMembershipAsync(userId, tenantId, cancellationToken);
            if (membership is null)
                return null;
            permissions = membership.IsOwner ? TenantPermissions.All : membership.Permissions;
        }
        else
        {
            var roles = await sender.Send(new GetUserRolesQuery(userId), cancellationToken);
            permissions = RolePermissionMap.For(roles);
        }

        var effective = permissions.ToHashSet(StringComparer.Ordinal);
        if (permissionCeiling is not null)
            effective.IntersectWith(permissionCeiling);
        return new AgentPrincipal(userId, membership, effective);
    }
}
