using System.Security.Claims;

namespace Shared.Core.Authorization;

/// <summary>
/// The <c>amr</c> (authentication methods references, RFC 8176) claim put into access tokens by Identity.
/// A token issued after a successful second factor carries <see cref="Mfa"/>.
/// <para>
/// JWT bearer maps the inbound <c>amr</c> claim to <see cref="MappedClaimType"/> by default, so always read it
/// through <see cref="IsMultiFactor"/> instead of looking up one claim type.
/// </para>
/// </summary>
public static class AuthenticationMethods
{
    public const string ClaimType = "amr";

    /// <summary>The claim type <c>amr</c> becomes when inbound claim mapping is on (the JwtBearer default).</summary>
    public const string MappedClaimType = "http://schemas.microsoft.com/claims/authnmethodsreferences";

    /// <summary>Password (knowledge factor).</summary>
    public const string Password = "pwd";

    /// <summary>One-time password from an authenticator app (TOTP).</summary>
    public const string Otp = "otp";

    /// <summary>Multiple factors were used.</summary>
    public const string Mfa = "mfa";

    public static bool IsMultiFactor(ClaimsPrincipal? principal) => principal is not null && IsMultiFactor(principal.Claims);

    public static bool IsMultiFactor(IEnumerable<Claim>? claims) =>
        claims?.Any(c => c.Value == Mfa && (c.Type == ClaimType || c.Type == MappedClaimType)) == true;
}

/// <summary>Machine-readable error codes returned by authorization failures.</summary>
public static class AuthorizationErrorCodes
{
    /// <summary>
    /// The caller has the permission, but their role requires a session that passed two-factor authentication
    /// (see Identity:TwoFactor:RequiredForRoles). Enroll via <c>POST /api/auth/2fa/setup</c> and sign in again.
    /// </summary>
    public const string MfaRequired = "mfa_required";

    /// <summary>Response header carrying the error code (also set in Warn mode as a heads-up).</summary>
    public const string HeaderName = "X-Auth-Error";
}
