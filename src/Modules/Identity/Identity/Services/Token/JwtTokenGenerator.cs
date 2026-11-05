using System.Globalization;
using System.Security.Cryptography;
using Identity.Authorization;
using Identity.Options;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity.Services.Token;

/// <summary>Custom claim types issued by <see cref="JwtTokenGenerator"/>.</summary>
public static class IdentityClaimTypes
{
    /// <summary>Hash of the user's security stamp at issuance (session revocation, see ITokenBlacklistService).</summary>
    public const string SecurityStampHash = "sstamp";

    /// <summary>
    /// Session id = the refresh-token <c>FamilyId</c>. Revoking one session blacklists this id (see
    /// ITokenBlacklistService) so its access token dies immediately instead of at expiry.
    /// </summary>
    public const string SessionId = "sid";

    public const string MustChangePassword = "must_change_password";
    public const string IsAdmin = "is_admin";
}

/// <summary>Impersonation token: issued to <c>act</c> = <paramref name="ActorUserId"/>, expiring at <paramref name="ExpiresAt"/>.</summary>
public sealed record ImpersonationGrant(Guid ActorUserId, DateTime ExpiresAt);

public interface IJwtTokenGenerator
{
    /// <summary>
    /// Creates a signed HS256 access token. <paramref name="roles"/> are the user's current roles. The token carries
    /// ROLES only - permissions are derived from them per request by <see cref="PermissionClaimsTransformation"/>.
    /// <paramref name="multiFactor"/> = the session passed a second factor: adds <c>amr=mfa</c> (+ <c>otp</c>).
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="roles">The user's platform roles.</param>
    /// <param name="multiFactor">The session passed a second factor.</param>
    /// <param name="tenant">
    /// Active tenant membership (multi-tenancy): adds <c>tid</c> (tenant id) and <c>tms</c> (membership stamp).
    /// Tenant permissions are NOT put in the token - they are resolved per request from the cached membership.
    /// </param>
    /// <param name="impersonation">Support impersonation: adds the <c>act</c> claim and the shorter expiry.</param>
    /// <param name="sessionId">The refresh-token family of the session: adds the <c>sid</c> claim.</param>
    Task<string> GenerateTokenAsync(ApplicationUser user, IReadOnlyCollection<string> roles, bool multiFactor = false,
        TenantMembershipInfo? tenant = null, ImpersonationGrant? impersonation = null, Guid? sessionId = null);

    /// <summary>512-bit random opaque refresh token.</summary>
    string GenerateRefreshToken();
}

public class JwtTokenGenerator(
    IOptions<JwtOptions> jwtOptions,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : IJwtTokenGenerator
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<string> GenerateTokenAsync(
        ApplicationUser user, IReadOnlyCollection<string> roles, bool multiFactor = false,
        TenantMembershipInfo? tenant = null, ImpersonationGrant? impersonation = null, Guid? sessionId = null)
    {
        var userClaims = await userManager.GetClaimsAsync(user);
        var now = timeProvider.GetUtcNow();

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? ""),
            new("phone_number", user.PhoneNumber ?? ""),
            new("phone_verified", user.IsPhoneNumberVerified.ToString()),
            new("email_verified", user.EmailConfirmed.ToString()),
            new("profile_complete", user.IsProfileComplete.ToString()),
            new("full_name", user.FullName),
            new(IdentityClaimTypes.MustChangePassword, user.MustChangePasswordOnNextLogin ? "true" : "false"),
            new(IdentityClaimTypes.SecurityStampHash, TokenHasher.HashSecurityStamp(user.SecurityStamp))
        };

        claims.AddRange(roles.Distinct(StringComparer.Ordinal).Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(AuthenticationMethodClaims(multiFactor));

        if (tenant is not null)
        {
            claims.Add(new Claim(TenantClaimTypes.TenantId, tenant.TenantId.ToString()));
            claims.Add(new Claim(TenantClaimTypes.MembershipStamp, tenant.Stamp));
        }

        if (sessionId is { } sid)
        {
            claims.Add(new Claim(IdentityClaimTypes.SessionId, sid.ToString()));
        }

        if (impersonation is not null)
        {
            claims.Add(new Claim(TenantClaimTypes.Actor, impersonation.ActorUserId.ToString()));
        }

        // Stored user claims last; never duplicate a (type, value) pair already present (e.g. a permission claim).
        var present = claims.Select(c => (c.Type, c.Value)).ToHashSet();
        claims.AddRange(userClaims.Where(c => present.Add((c.Type, c.Value))));

        if (roles.Contains(RoleConstants.Admin) || roles.Contains(RoleConstants.SuperAdmin))
        {
            claims.Add(new Claim(IdentityClaimTypes.IsAdmin, "true"));
        }

        var credentials = new SigningCredentials(JwtSigningKeys.Current(_jwtOptions), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: impersonation?.ExpiresAt ?? now.UtcDateTime.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Only MFA sessions carry <c>amr</c>; the first factor may be a password or an external provider.</summary>
    private static IEnumerable<Claim> AuthenticationMethodClaims(bool multiFactor) => multiFactor
        ?
        [
            new Claim(AuthenticationMethods.ClaimType, AuthenticationMethods.Otp),
            new Claim(AuthenticationMethods.ClaimType, AuthenticationMethods.Mfa)
        ]
        : [];

    public string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
