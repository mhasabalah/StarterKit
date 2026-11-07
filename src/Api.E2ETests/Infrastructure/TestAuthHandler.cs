using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy;

namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Test authentication scheme. Reads the authenticated user id from the <c>X-Test-UserId</c> header and roles
/// (comma-separated) from <c>X-Test-Roles</c>. When no user id header is present the request is treated as anonymous
/// so that protected endpoints return 401.
/// <list type="bullet">
/// <item>Permission claims are NOT added here: like for a real token, the production
/// <see cref="PermissionClaimsTransformation"/> derives them from the roles. <c>X-Test-Permissions</c>
/// (comma-separated) grants EXTRA permissions on top (e.g. a permission no role has).</item>
/// <item><c>X-Test-Mfa: true</c> adds <c>amr=mfa</c> (a session that passed 2FA).</item>
/// <item>A request with <c>Authorization: Bearer ...</c> and no <c>X-Test-UserId</c> is forwarded to the REAL JWT
/// bearer scheme (see <see cref="E2ETestFactory"/>), so tests can exercise real login tokens end to end.</item>
/// </list>
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string UserIdHeader = "X-Test-UserId";
    public const string RolesHeader = "X-Test-Roles";
    public const string PermissionsHeader = "X-Test-Permissions";
    public const string MfaHeader = "X-Test-Mfa";

    /// <summary>Active tenant of the session (adds the <c>tid</c> claim, as a tenant-bound JWT would).</summary>
    public const string TenantHeader = "X-Test-TenantId";

    /// <summary>Optional membership stamp (<c>tms</c>) - omit to skip the stale-token check.</summary>
    public const string MembershipStampHeader = "X-Test-MembershipStamp";

    /// <summary>Adds <c>email</c> + <c>email_verified=True</c> claims (invitation acceptance).</summary>
    public const string EmailHeader = "X-Test-Email";

    /// <summary>Adds the <c>act</c> claim: the request is an IMPERSONATED session of the given platform user.</summary>
    public const string ActorHeader = "X-Test-Actor";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserIdHeader, out var userIdValues))
        {
            // Anonymous — let the authorization layer return 401 on protected routes.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = userIdValues.ToString();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userId)
        };

        var roles = Split(Request.Headers[RolesHeader].ToString());
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        claims.AddRange(Split(Request.Headers[PermissionsHeader].ToString())
            .Distinct(StringComparer.Ordinal)
            .Select(p => new Claim(Permissions.ClaimType, p)));

        if (string.Equals(Request.Headers[MfaHeader].ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(AuthenticationMethods.ClaimType, AuthenticationMethods.Mfa));
        }

        if (Request.Headers[TenantHeader].ToString() is { Length: > 0 } tenantId)
        {
            claims.Add(new Claim(TenantClaimTypes.TenantId, tenantId));
        }

        if (Request.Headers[ActorHeader].ToString() is { Length: > 0 } actor)
        {
            claims.Add(new Claim(TenantClaimTypes.Actor, actor));
        }

        if (Request.Headers[MembershipStampHeader].ToString() is { Length: > 0 } stamp)
        {
            claims.Add(new Claim(TenantClaimTypes.MembershipStamp, stamp));
        }

        if (Request.Headers[EmailHeader].ToString() is { Length: > 0 } email)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
            claims.Add(new Claim("email_verified", "True"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static IReadOnlyList<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
