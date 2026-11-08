using Identity.Contracts.Auth.Commands;
using System.Text.Json.Serialization;
using Shared.Contracts.Attributes;

namespace Identity.Contracts.Sso;

// Enterprise single sign-on (per tenant). A tenant admin configures a connection to the company's identity provider
// (Entra ID, Okta, Google Workspace, ADFS, Keycloak, ...), proves ownership of the email domains with DNS, and
// activates it. Users then sign in at the IdP; accounts are linked or created just in time.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SsoProtocol
{
    [LocalizedDescription("OpenID Connect", "OpenID Connect")]
    Oidc = 0,

    [LocalizedDescription("SAML 2.0", "SAML 2.0")]
    Saml = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SsoConnectionStatus
{
    /// <summary>Being configured; no one can sign in with it yet.</summary>
    [LocalizedDescription("Draft", "مسودة")]
    Draft = 0,

    [LocalizedDescription("Active", "مفعّل")]
    Active = 1,

    [LocalizedDescription("Disabled", "معطّل")]
    Disabled = 2
}

/// <summary>Write model shared by create and update. Secrets are write-only (never returned).</summary>
public sealed record SsoConnectionInput
{
    public string Name { get; init; } = string.Empty;
    public SsoProtocol Protocol { get; init; }

    // ----- OIDC -----

    /// <summary>Issuer URL; discovery is <c>{Authority}/.well-known/openid-configuration</c>. HTTPS only.</summary>
    public string? OidcAuthority { get; init; }

    public string? OidcClientId { get; init; }

    /// <summary>Write-only. Null on update = keep the stored secret.</summary>
    public string? OidcClientSecret { get; init; }

    /// <summary>Extra scopes besides <c>openid email profile</c> (space separated).</summary>
    public string? OidcExtraScopes { get; init; }

    // ----- SAML -----

    public string? SamlIdpEntityId { get; init; }

    /// <summary>The IdP's SingleSignOnService URL (HTTP-Redirect binding).</summary>
    public string? SamlIdpSsoUrl { get; init; }

    /// <summary>Optional metadata URL: the IdP certificates and SSO URL are refreshed from it every few hours (key rollover; entity id must match).</summary>
    public string? SamlIdpMetadataUrl { get; init; }

    /// <summary>IdP signing certificates (Base64 DER or PEM). Several are allowed during a rollover.</summary>
    public IReadOnlyList<string>? SamlIdpCertificates { get; init; }

    /// <summary>Accept unsolicited (IdP-initiated) responses. Off by default: they cannot be bound to a request.</summary>
    public bool SamlAllowIdpInitiated { get; init; }

    /// <summary>Refuse a metadata refresh without a valid signature. Null on update = keep the current setting.</summary>
    public bool? SamlRequireSignedMetadata { get; init; }

    /// <summary>
    /// Optional certificates that sign the metadata (Base64 DER or PEM), when they differ from the IdP signing
    /// certificates. Setting any also requires a signature. Null on update = keep, empty = clear.
    /// </summary>
    public IReadOnlyList<string>? SamlMetadataSigningCertificates { get; init; }

    // ----- mapping & policy -----

    /// <summary>Claim / attribute holding the email. Default: <c>email</c> (OIDC) or the standard email attributes.</summary>
    public string? EmailClaim { get; init; }

    public string? GivenNameClaim { get; init; }
    public string? FamilyNameClaim { get; init; }

    /// <summary>Create accounts and memberships on first sign-in.</summary>
    public bool JitProvisioning { get; init; } = true;

    /// <summary>Tenant roles granted to just-in-time provisioned members.</summary>
    public IReadOnlyList<Guid>? DefaultRoleIds { get; init; }

    /// <summary>Users of the verified domains must sign in through this connection (password login is refused).</summary>
    public bool EnforceSso { get; init; }

    /// <summary>Count the session as MFA when the IdP asserts it (OIDC <c>amr</c>/<c>acr</c>, SAML AuthnContext).</summary>
    public bool TrustIdpMfa { get; init; }
}

public sealed record SsoDomainDto(string Domain, bool Verified, string VerificationRecordName, string VerificationRecordValue,
    DateTime? VerifiedAt);

public sealed record SsoConnectionDto(
    Guid Id,
    string Name,
    SsoProtocol Protocol,
    SsoConnectionStatus Status,
    string? OidcAuthority,
    string? OidcClientId,
    bool OidcClientSecretSet,
    string? OidcExtraScopes,
    string? SamlIdpEntityId,
    string? SamlIdpSsoUrl,
    string? SamlIdpMetadataUrl,
    IReadOnlyList<SsoCertificateDto> SamlIdpCertificates,
    bool SamlAllowIdpInitiated,
    DateTime? SamlMetadataRefreshedAt,
    string? SamlMetadataLastError,
    DateTime? SamlCertificatesNotAfter,
    bool SamlRequireSignedMetadata,
    IReadOnlyList<SsoCertificateDto> SamlMetadataSigningCertificates,
    // Effective rule: true when SamlRequireSignedMetadata is set OR a metadata-signing key is pinned. Show this one.
    bool SamlMetadataSignatureRequired,
    string? EmailClaim,
    string? GivenNameClaim,
    string? FamilyNameClaim,
    bool JitProvisioning,
    IReadOnlyList<Guid> DefaultRoleIds,
    bool EnforceSso,
    bool TrustIdpMfa,
    IReadOnlyList<SsoDomainDto> Domains,
    SsoServiceProviderDto ServiceProvider,
    DateTime? LastLoginAt,
    long Version);

public sealed record SsoCertificateDto(string Subject, string Thumbprint, DateTime NotAfter);

/// <summary>What the customer's IdP admin pastes into their IdP (redirect URI / ACS URL / entity id / metadata).</summary>
public sealed record SsoServiceProviderDto(
    string? OidcRedirectUri,
    string? SamlEntityId,
    string? SamlAcsUrl,
    string? SamlMetadataUrl);

// ----- tenant admin -----

public sealed record GetSsoConnectionsQuery : IQuery<List<SsoConnectionDto>>;

public sealed record GetSsoConnectionByIdQuery(Guid Id) : IQuery<SsoConnectionDto>;

public sealed record CreateSsoConnectionCommand(SsoConnectionInput Input) : ICommand<SsoConnectionDto>;

public sealed record UpdateSsoConnectionCommand(Guid Id, SsoConnectionInput Input, long? Version = null)
    : ICommand<SsoConnectionDto>;

/// <summary>Draft → Active. Needs a complete protocol configuration and at least one verified domain.</summary>
public sealed record ActivateSsoConnectionCommand(Guid Id, long? Version = null) : ICommand<SsoConnectionDto>;

public sealed record DisableSsoConnectionCommand(Guid Id, long? Version = null) : ICommand<SsoConnectionDto>;

public sealed record DeleteSsoConnectionCommand(Guid Id) : ICommand<DeleteSsoConnectionResult>;

public sealed record DeleteSsoConnectionResult(Guid Id);

/// <summary>
/// "Refresh now": re-reads the SAML metadata URL of the connection. Failures are reported in the result
/// (<see cref="RefreshSamlMetadataResult.Error"/>), not as an HTTP error; read the connection for the full state.
/// </summary>
public sealed record RefreshSamlMetadataCommand(Guid Id) : ICommand<RefreshSamlMetadataResult>;

/// <param name="Id">Connection.</param>
/// <param name="Outcome"><c>Updated</c>, <c>Unchanged</c> or <c>Failed</c>.</param>
/// <param name="Error">Stable code when failed (<c>metadata_entity_id_mismatch</c>, <c>metadata_fetch_failed</c>, ...).</param>
/// <param name="RefreshedAt">Last successful refresh (UTC).</param>
public sealed record RefreshSamlMetadataResult(Guid Id, string Outcome, string? Error, DateTime? RefreshedAt);

/// <summary>Adds an email domain; returns the TXT record to publish.</summary>
public sealed record AddSsoDomainCommand(Guid ConnectionId, string Domain) : ICommand<SsoDomainDto>;

/// <summary>Checks the TXT record now. 409 <c>sso_domain_unverified</c> while it is missing.</summary>
public sealed record VerifySsoDomainCommand(Guid ConnectionId, string Domain) : ICommand<SsoDomainDto>;

public sealed record RemoveSsoDomainCommand(Guid ConnectionId, string Domain) : ICommand<RemoveSsoDomainResult>;

public sealed record RemoveSsoDomainResult(string Domain);

/// <summary>
/// "Test connection": a real IdP round trip (Draft connections too) that signs NOBODY in. The callback returns to
/// <paramref name="ReturnUrl"/> with <c>sso_test={code}</c>; read the report with <see cref="GetSsoTestReportQuery"/>.
/// </summary>
public sealed record StartSsoTestCommand(Guid ConnectionId, string ReturnUrl) : ICommand<SsoRedirect>;

public sealed record GetSsoTestReportQuery(string Code) : IQuery<SsoTestReportDto>;

/// <summary>What the IdP asserted, for checking claim/attribute mappings before activation.</summary>
public sealed record SsoTestReportDto(
    Guid ConnectionId,
    string Subject,
    string? Email,
    bool EmailOnVerifiedDomain,
    bool MultiFactor,
    string? GivenName,
    string? FamilyName,
    IReadOnlyDictionary<string, string> Claims);

/// <summary>Platform staff: every tenant's connections (support / security review).</summary>
public sealed record GetAllSsoConnectionsQuery : BaseQuery, IQuery<PaginatedResult<SsoConnectionSummaryDto>>
{
    public Guid? TenantId { get; init; }
}

public sealed record SsoConnectionSummaryDto(Guid Id, Guid TenantId, string Name, SsoProtocol Protocol,
    SsoConnectionStatus Status, bool EnforceSso, IReadOnlyList<string> VerifiedDomains, DateTime? LastLoginAt);

// ----- sign-in -----

/// <summary>Home-realm discovery: does this email belong to an SSO domain?</summary>
public sealed record DiscoverSsoQuery(string Email) : IQuery<SsoDiscoveryResult>;

/// <param name="SsoAvailable">An active connection covers the email's domain.</param>
/// <param name="SsoRequired">That connection enforces SSO: password sign-in will be refused.</param>
public sealed record SsoDiscoveryResult(bool SsoAvailable, bool SsoRequired, Guid? ConnectionId, SsoProtocol? Protocol);

/// <summary>Builds the IdP redirect. <paramref name="ReturnUrl"/> must be on an allowed frontend origin.</summary>
public sealed record StartSsoLoginCommand(Guid ConnectionId, string ReturnUrl, string? LoginHint = null)
    : ICommand<SsoRedirect>;

/// <summary>Where to send the browser: GET <see cref="Url"/>, or auto-POST <see cref="FormFields"/> to it.</summary>
public sealed record SsoRedirect(string Url, IReadOnlyDictionary<string, string>? FormFields = null)
{
    /// <summary>
    /// Server-internal: the value the start endpoint puts in an HttpOnly cookie, so the callback only completes in the
    /// browser that started the sign-in (login-CSRF defence). Never serialized.
    /// </summary>
    [JsonIgnore]
    public string? BrowserBinding { get; init; }
}

/// <summary>OIDC redirect URI callback (<c>code</c> + <c>state</c>, or <c>error</c>).</summary>
public sealed record CompleteOidcLoginCommand(
    string? Code, string? State, string? Error, string? ErrorDescription, string? BrowserBinding = null)
    : ICommand<SsoCallbackResult>;

/// <summary>SAML Assertion Consumer Service (HTTP-POST binding).</summary>
public sealed record CompleteSamlLoginCommand(
    Guid ConnectionId, string SamlResponse, string? RelayState, string? BrowserBinding = null)
    : ICommand<SsoCallbackResult>;

/// <summary>Where the browser goes after the callback: the app, carrying a one-time code (or an error code).</summary>
public sealed record SsoCallbackResult(string RedirectUrl);

/// <summary>SAML SP metadata XML of a connection (any status, so it can be uploaded before activation).</summary>
public sealed record GetSsoSamlMetadataQuery(Guid ConnectionId) : IQuery<string>;

/// <summary>Trades the one-time code from the callback for a session (same result shape as password login).</summary>
public sealed record ExchangeSsoCodeCommand(string Code) : ICommand<LoginResult>;
