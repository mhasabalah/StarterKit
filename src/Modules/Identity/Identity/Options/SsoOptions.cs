namespace Identity.Options;

/// <summary>Enterprise SSO (SAML 2.0 / OIDC per tenant) and SCIM settings, section <c>Identity:Sso</c>.</summary>
public sealed class SsoOptions
{
    public const string SectionName = "Identity:Sso";

    /// <summary>Master switch for SSO sign-in and SCIM (the admin endpoints stay readable).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The PUBLIC base URL of this API as the identity provider sees it, e.g. <c>https://api.example.com</c>.
    /// Redirect URI, SAML ACS URL and SP entity id are derived from it, so it must never change once customers have
    /// configured their IdP. Null = the request's scheme and host (development only).
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>
    /// Frontend origins a sign-in may return to (<c>returnUrl</c>), e.g. <c>https://app.example.com</c>. A return URL
    /// on any other origin is refused (open-redirect guard). Empty = <c>Application:FrontendBaseUrl</c> only.
    /// </summary>
    public string[] AllowedReturnOrigins { get; set; } = [];

    /// <summary>How long the IdP round trip may take (state, nonce, PKCE verifier lifetime).</summary>
    public int LoginTimeoutMinutes { get; set; } = 10;

    /// <summary>Lifetime of the one-time code handed to the frontend after the callback.</summary>
    public int ExchangeCodeLifetimeSeconds { get; set; } = 60;

    /// <summary>
    /// Allow IdP URLs that are not HTTPS or that resolve to private networks (a local Keycloak in development).
    /// Never in production: tenant admins type these URLs, so this is an SSRF boundary.
    /// </summary>
    public bool AllowInsecureIdentityProviders { get; set; }

    /// <summary>Accepted clock skew for IdP tokens and assertions.</summary>
    public int ClockSkewSeconds { get; set; } = 120;

    /// <summary>
    /// Platform staff (SuperAdmin, Admin, ...) and tenant owners may still use a password when their domain enforces
    /// SSO - the break-glass path if the IdP is down or misconfigured.
    /// </summary>
    public bool AllowBreakGlassPasswordLogin { get; set; } = true;

    /// <summary>Cron of the recurring SAML metadata refresh (connections with a metadata URL). Default: every 6 hours.</summary>
    public string MetadataRefreshCron { get; set; } = "0 */6 * * *";

    /// <summary>Turns the recurring SAML metadata refresh off ("refresh now" in the admin API still works).</summary>
    public bool MetadataRefreshEnabled { get; set; } = true;

    /// <summary>Cron of the SAML certificate expiry check. Default: daily at 07:00 UTC.</summary>
    public string CertificateExpiryCheckCron { get; set; } = "0 7 * * *";

    /// <summary>Turns the SAML certificate expiry alerts off (the daily check does not run).</summary>
    public bool CertificateExpiryAlertsEnabled { get; set; } = true;

    /// <summary>
    /// SSO administrators are alerted once for each of these "days before the IdP certificates expire" thresholds
    /// (and once more for every smaller one that is crossed). The code default is EMPTY on purpose: the configuration
    /// binder APPENDS to a non-empty default array, so a configured <c>[14, 3]</c> would become <c>[30, 14, 7, 1, 14, 3]</c>.
    /// An empty value is replaced by <see cref="DefaultCertificateExpiryAlertDays"/> in a post-configure step.
    /// </summary>
    public int[] CertificateExpiryAlertDays { get; set; } = [];

    public static readonly int[] DefaultCertificateExpiryAlertDays = [30, 14, 7, 1];
}
