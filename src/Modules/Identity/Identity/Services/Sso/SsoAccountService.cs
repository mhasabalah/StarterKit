using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Domains.Users.Services;
using Identity.Services.Security;
using Notification.Contracts.Email.Models;
using Shared.Core.MultiTenancy;
using Tenancy.Contracts.Members;

namespace Identity.Services.Sso;

/// <summary>
/// Turns a verified IdP identity into a local account and tenant membership.
/// <list type="number">
/// <item>Known link <c>(sso:{connectionId}, subject)</c> → that user. Links never move between users.</item>
/// <item>Otherwise the email must be on a VERIFIED domain of the connection (and not explicitly unverified by the
/// IdP). An existing account with that email is linked (security email to the user); without one, a new account is
/// created when JIT provisioning is on.</item>
/// <item>The membership in the connection's tenant is ensured through Tenancy (<see cref="ProvisionMemberCommand"/>).
/// A member suspended by the tenant or by SCIM stays suspended: SSO never reactivates anyone.</item>
/// </list>
/// </summary>
public sealed class SsoAccountService(
    UserManager<ApplicationUser> userManager,
    ISender sender,
    ITenantStore tenantStore,
    ITenantMembershipResolver memberships,
    ISecurityAlertSender securityAlerts,
    ILogger<SsoAccountService> logger)
{
    public static string LoginProvider(Guid connectionId) => $"sso:{connectionId:N}";

    public async Task<ApplicationUser> ResolveAsync(SsoConnection connection, SsoIdentity identity, CancellationToken ct)
    {
        var provider = LoginProvider(connection.Id);
        var user = await userManager.FindByLoginAsync(provider, identity.Subject);

        if (user is null)
        {
            if (identity.Email is null || !identity.EmailVerified || !connection.CoversVerified(identity.Email))
            {
                logger.LogWarning(
                    "SSO sign-in refused for connection {ConnectionId}: email missing, unverified or outside the verified domains",
                    connection.Id);
                throw new SsoLoginFailedException("domain_not_allowed");
            }

            user = await userManager.FindByEmailAsync(identity.Email);
            var existing = user is not null;
            if (user is null)
            {
                if (!connection.JitProvisioning)
                    throw new SsoLoginFailedException("not_provisioned");

                user = await CreateUserAsync(identity);
            }
            else
            {
                await EnsureNotStaffAsync(user, connection);
            }

            var linked = await userManager.AddLoginAsync(user, new UserLoginInfo(provider, identity.Subject, connection.Name));
            if (!linked.Succeeded)
                throw new SsoLoginFailedException("link_failed: " + string.Join(",", linked.Errors.Select(e => e.Code)));

            if (existing)
                await securityAlerts.SendAsync(user, SecurityAlertEvents.SsoIdentityLinked, connection.Name, ct);

            logger.LogInformation("SSO identity of connection {ConnectionId} linked to user {UserId}", connection.Id, user.Id);
        }

        if (!user.IsActive || await userManager.IsLockedOutAsync(user))
            throw new SsoLoginFailedException("account_disabled");

        // Checked on EVERY sign-in, not only at linking: a role granted after the link must not be reachable via an IdP.
        await EnsureNotStaffAsync(user, connection);

        await EnsureMembershipAsync(connection, user.Id, ct);
        return user;
    }

    /// <summary>
    /// A tenant's IdP vouches for tenant users only. Platform staff (SuperAdmin, Admin, Finance, Support) carry global
    /// roles, so letting a customer-controlled IdP sign them in (and assert their MFA) would hand platform access to
    /// whoever administers that IdP. Staff always use their own credentials and 2FA.
    /// </summary>
    private async Task EnsureNotStaffAsync(ApplicationUser user, SsoConnection connection)
    {
        if (RoleHierarchy.IsStaff(await userManager.GetRolesAsync(user)))
        {
            logger.LogWarning("SSO sign-in of platform staff account {UserId} through connection {ConnectionId} refused",
                user.Id, connection.Id);
            throw new SsoLoginFailedException("staff_account");
        }
    }

    private async Task<ApplicationUser> CreateUserAsync(SsoIdentity identity)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = identity.Email,
            Email = identity.Email,
            EmailConfirmed = true, // asserted by the IdP for a domain the tenant proved it owns
            FirstName = Truncate(identity.GivenName) ?? string.Empty,
            LastName = Truncate(identity.FamilyName) ?? string.Empty,
            IsActive = true,
            IsProfileComplete = true
        };

        var created = await userManager.CreateAsync(user); // no password: the IdP is the credential
        if (!created.Succeeded)
            throw new SsoLoginFailedException("create_failed: " + string.Join(",", created.Errors.Select(e => e.Code)));

        await userManager.AddToRoleAsync(user, RoleConstants.User);
        logger.LogInformation("SSO just-in-time provisioned user {UserId}", user.Id);
        return user;
    }

    private async Task EnsureMembershipAsync(SsoConnection connection, Guid userId, CancellationToken ct)
    {
        var tenant = await tenantStore.FindByIdAsync(connection.TenantId, ct)
                     ?? throw new SsoLoginFailedException("tenant_unavailable");

        var existing = await memberships.GetMembershipAsync(userId, connection.TenantId, ct);
        if (existing is not null)
            return; // active or suspended: SSO does not change it (session issuance enforces the status)

        if (!connection.JitProvisioning)
            throw new SsoLoginFailedException("not_provisioned");

        using (TenantScope.Begin(tenant))
        {
            await sender.Send(new ProvisionMemberCommand(userId, MemberProvisioningSource.Sso, connection.DefaultRoleIds), ct);
        }

        await memberships.InvalidateAsync(connection.TenantId, userId, ct);
    }

    private static string? Truncate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > 100 ? value.Trim()[..100] : value.Trim();
}
