using Identity.Resources;
using Tenancy.Contracts.Roles;

namespace Identity.Services.Sso;

/// <summary>A role id that is not a role of the current tenant.</summary>
public sealed class UnknownTenantRoleException : BadRequestException
{
    public UnknownTenantRoleException(Guid roleId)
        : base(IdentityText.Get("Sso.UnknownTenantRole", roleId)) => ErrorCode = "tenant_role_not_found";
}

/// <summary>
/// Checks that role ids an admin assigns to directory-driven memberships (SSO default roles, SCIM group mappings)
/// are roles of the CURRENT tenant, through Tenancy's contract (Identity never reads Tenancy data directly). Without
/// it a stale or foreign role id would be stored and silently ignored on every provisioning.
/// </summary>
public interface ITenantRoleGuard
{
    Task EnsureTenantRolesAsync(IEnumerable<Guid> roleIds, CancellationToken ct);
}

public sealed class TenantRoleGuard(ISender sender) : ITenantRoleGuard
{
    public async Task EnsureTenantRolesAsync(IEnumerable<Guid> roleIds, CancellationToken ct)
    {
        var requested = roleIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (requested.Count == 0)
            return;

        var known = (await sender.Send(new GetTenantRolesQuery(), ct)).Select(r => r.Id).ToHashSet();
        var unknown = requested.FirstOrDefault(id => !known.Contains(id));
        if (unknown != Guid.Empty)
            throw new UnknownTenantRoleException(unknown);
    }
}
