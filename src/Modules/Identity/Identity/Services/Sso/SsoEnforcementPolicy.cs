using Identity.Data.Repository.Sso;
using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Domains.Users.Services;
using Identity.Options;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy;

namespace Identity.Services.Sso;

/// <summary>
/// "Must this user sign in through their company IdP?" Applied by every NON-SSO sign-in path (password, passkey,
/// social login) and by password reset, so offboarding at the IdP really locks the user out.
/// Break-glass: platform staff and the owner of the enforcing tenant keep their other methods
/// (<c>Identity:Sso:AllowBreakGlassPasswordLogin</c>); otherwise a broken IdP would lock everyone out.
/// Enforcement applies only while SSO itself works: platform switch on, connection active, tenant entitled.
/// </summary>
public interface ISsoEnforcementPolicy
{
    /// <summary>The active connection that covers the domain of <paramref name="email"/>, if any.</summary>
    Task<SsoConnection?> FindConnectionForEmailAsync(string? email, CancellationToken ct = default);

    /// <summary>Throws <see cref="SsoRequiredException"/> (403 <c>sso_required</c>) when the user must use SSO.</summary>
    Task EnsureNonSsoSignInAllowedAsync(ApplicationUser user, CancellationToken ct = default);

    /// <summary>False when a password reset must not be offered (the IdP owns the credential).</summary>
    Task<bool> IsPasswordResetAllowedAsync(ApplicationUser user, CancellationToken ct = default);
}

public sealed class SsoEnforcementPolicy(
    ISsoConnectionRepository connections,
    UserManager<ApplicationUser> userManager,
    ITenantMembershipResolver memberships,
    ISsoEntitlement entitlement,
    IOptions<SsoOptions> options) : ISsoEnforcementPolicy
{
    public async Task<SsoConnection?> FindConnectionForEmailAsync(string? email, CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            return null;

        var domain = SsoDomain.FromEmail(email);
        var connection = domain is null ? null : await connections.FindActiveByVerifiedDomainAsync(domain, ct);

        // A tenant whose plan lost the sso feature has no usable SSO: discovery stops offering it AND enforcement
        // lapses, so its users fall back to their other sign-in methods instead of being locked out.
        return connection is not null && await entitlement.IsEntitledAsync(connection.TenantId, ct) ? connection : null;
    }

    public async Task EnsureNonSsoSignInAllowedAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var connection = await EnforcingConnectionAsync(user, ct);
        if (connection is not null)
            throw new SsoRequiredException(connection.Id);
    }

    public async Task<bool> IsPasswordResetAllowedAsync(ApplicationUser user, CancellationToken ct = default) =>
        await EnforcingConnectionAsync(user, ct) is null;

    private async Task<SsoConnection?> EnforcingConnectionAsync(ApplicationUser user, CancellationToken ct)
    {
        var connection = await FindConnectionForEmailAsync(user.Email, ct);
        if (connection is null || !connection.EnforceSso)
            return null;

        if (options.Value.AllowBreakGlassPasswordLogin)
        {
            if (RoleHierarchy.IsStaff(await userManager.GetRolesAsync(user)))
                return null;

            var membership = await memberships.GetMembershipAsync(user.Id, connection.TenantId, ct);
            if (membership?.IsOwner == true)
                return null;
        }

        return connection;
    }
}
