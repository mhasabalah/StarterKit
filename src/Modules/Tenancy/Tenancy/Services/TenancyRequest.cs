using Microsoft.Extensions.Options;

namespace Tenancy.Services;

/// <summary>
/// Per-request guards shared by the Tenancy handlers: current tenant/user, ownership, and the anti-escalation rule
/// "you can only grant what you hold".
/// </summary>
public sealed class TenancyRequest(
    ITenantContext tenantContext,
    IHttpContextService httpContextService,
    IOptions<MultiTenancyOptions> options)
{
    public bool MultiTenancyEnabled => options.Value.Enabled;

    public Guid TenantId => tenantContext.TenantId ?? throw new TenantNotResolvedException();

    public Guid UserId => httpContextService.GetUserIdAsGuid()
                          ?? throw new UnauthorizedException("Authentication is required.");

    public TenantMembershipInfo? Membership =>
        tenantContext.Membership is { } m && m.TenantId == tenantContext.TenantId ? m : null;

    public BranchScope Branches => tenantContext.Branches;

    /// <summary>Platform administrator (acts for the Default tenant in single-tenant mode).</summary>
    public bool IsPlatformAdmin
    {
        get
        {
            var roles = httpContextService.GetRoles().ToHashSet(StringComparer.Ordinal);
            return roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.SuperAdmin);
        }
    }

    /// <summary>Owner of the current tenant (single-tenant mode: a platform admin).</summary>
    public bool IsOwner => Membership?.IsOwner == true || (!MultiTenancyEnabled && IsPlatformAdmin);

    public void EnsureOwner()
    {
        if (!IsOwner) throw new ForbiddenException("Only the tenant owner can do this.");
    }

    /// <summary>The caller's effective tenant permissions (owner = all; single-tenant: platform claims).</summary>
    public IReadOnlySet<string> Permissions
    {
        get
        {
            if (IsOwner) return TenantPermissions.All.ToHashSet(StringComparer.Ordinal);
            if (Membership is { } m) return m.Permissions;
            return MultiTenancyEnabled
                ? new HashSet<string>()
                : httpContextService.GetClaimValues(Shared.Core.Authorization.Permissions.ClaimType)
                    .ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>Privilege-escalation guard: a non-owner may only grant permissions it holds itself.</summary>
    public void EnsureCanGrant(IEnumerable<string> permissions)
    {
        if (IsOwner) return;
        var missing = permissions.Except(Permissions, StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
            throw new ForbiddenException($"You cannot grant permissions you do not hold: {string.Join(", ", missing)}.");
    }

    /// <summary>A branch-restricted caller may only touch branches inside its scope.</summary>
    public void EnsureBranchesInScope(IEnumerable<Guid> branchIds)
    {
        if (!Branches.IsRestricted) return;
        var outside = branchIds.Where(id => !Branches.Allows(id)).ToList();
        if (outside.Count > 0)
            throw new TenantAccessDeniedException(TenantErrors.BranchNotAllowed,
                "one or more branches are outside your branch scope.");
    }
}
