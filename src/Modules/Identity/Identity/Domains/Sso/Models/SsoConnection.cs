using System.Security.Cryptography;
using Identity.Contracts.Sso;
using Identity.Domains.Sso.Events;
using Identity.Domains.Sso.Exceptions;
using Shared.Core.Auditing;

namespace Identity.Domains.Sso.Models;

/// <summary>
/// A tenant's connection to its corporate identity provider (OIDC or SAML 2.0). Control-plane data owned by Identity
/// (like sessions and API keys), scoped by <see cref="TenantId"/> explicitly in every query.
/// <para>Invariants:</para>
/// <list type="bullet">
/// <item>Only an <see cref="SsoConnectionStatus.Active"/> connection can sign anyone in.</item>
/// <item>Activation needs a complete configuration for its protocol and at least one verified domain.</item>
/// <item>Email-based account linking and JIT provisioning only ever apply to <b>verified</b> domains, so a tenant
/// cannot claim someone else's users by configuring their domain.</item>
/// </list>
/// The OIDC client secret is stored encrypted (Data Protection) and never leaves the server.
/// </summary>
public class SsoConnection : Aggregate<Guid>, IAuditable
{
    public const int NameMaxLength = 100;
    public const int UrlMaxLength = 2048;
    public const int ClaimMaxLength = 256;
    public const int MaxDomains = 20;
    public const int MaxCertificates = 4;
    public const int MaxDefaultRoles = 20;

    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public SsoProtocol Protocol { get; private set; }
    public SsoConnectionStatus Status { get; private set; }

    public string? OidcAuthority { get; private set; }
    public string? OidcClientId { get; private set; }

    [AuditIgnore]
    public string? OidcClientSecretProtected { get; private set; }

    public string? OidcExtraScopes { get; private set; }

    public string? SamlIdpEntityId { get; private set; }
    public string? SamlIdpSsoUrl { get; private set; }
    public string? SamlIdpMetadataUrl { get; private set; }

    /// <summary>Base64 DER of the IdP signing certificates (public keys only).</summary>
    public List<string> SamlIdpCertificates { get; private set; } = [];

    public bool SamlAllowIdpInitiated { get; private set; }

    public string? EmailClaim { get; private set; }
    public string? GivenNameClaim { get; private set; }
    public string? FamilyNameClaim { get; private set; }

    public bool JitProvisioning { get; private set; }
    public List<Guid> DefaultRoleIds { get; private set; } = [];
    public bool EnforceSso { get; private set; }
    public bool TrustIdpMfa { get; private set; }

    [AuditIgnore]
    public DateTime? LastLoginAt { get; private set; }

    private readonly List<SsoDomain> _domains = [];
    public IReadOnlyList<SsoDomain> Domains => _domains;

    private SsoConnection()
    {
    }

    public static SsoConnection Create(Guid tenantId, SsoConnectionInput input, string? protectedClientSecret)
    {
        if (tenantId == Guid.Empty)
            throw new InvalidSsoConnectionException("Sso.Connection.TenantRequired");

        var connection = new SsoConnection
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Protocol = input.Protocol,
            Status = SsoConnectionStatus.Draft
        };
        connection.Apply(input, protectedClientSecret);
        return connection;
    }

    /// <summary>Updates the configuration. The protocol is fixed; create another connection to switch protocols.</summary>
    public void Update(SsoConnectionInput input, string? protectedClientSecret)
    {
        if (input.Protocol != Protocol)
            throw new InvalidSsoConnectionException("Sso.Connection.ProtocolFixed");

        var previousAuthority = OidcAuthority;
        Apply(input, protectedClientSecret);

        // An active connection must stay usable after the edit.
        if (Status == SsoConnectionStatus.Active)
            EnsureComplete();

        RaiseChanged(previousAuthority);
    }

    public void Activate()
    {
        EnsureComplete();
        if (!_domains.Any(d => d.IsVerified))
            throw new InvalidSsoConnectionException("Sso.Connection.VerifyDomainFirst");

        Status = SsoConnectionStatus.Active;
        RaiseChanged();
    }

    public void Disable()
    {
        Status = SsoConnectionStatus.Disabled;
        RaiseChanged();
    }

    public SsoDomain AddDomain(string domain)
    {
        var normalized = SsoDomain.Normalize(domain);
        if (_domains.Any(d => d.Domain == normalized))
            throw new InvalidSsoConnectionException("Sso.Connection.DomainAlreadyAdded", normalized);
        if (_domains.Count >= MaxDomains)
            throw new InvalidSsoConnectionException("Sso.Connection.TooManyDomains", MaxDomains);

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var added = SsoDomain.Create(Id, normalized, token);
        _domains.Add(added);
        return added;
    }

    public SsoDomain GetDomain(string domain)
    {
        var normalized = SsoDomain.Normalize(domain);
        return _domains.FirstOrDefault(d => d.Domain == normalized) ?? throw new SsoDomainNotFoundException(normalized);
    }

    public void MarkDomainVerified(string domain, DateTime now)
    {
        GetDomain(domain).MarkVerified(now);
        RaiseChanged();
    }

    public void RemoveDomain(string domain)
    {
        var existing = GetDomain(domain);
        if (Status == SsoConnectionStatus.Active && existing.IsVerified && _domains.Count(d => d.IsVerified) == 1)
            throw new InvalidSsoConnectionException("Sso.Connection.LastVerifiedDomain");

        _domains.Remove(existing);
        RaiseChanged();
    }

    /// <summary>True when <paramref name="email"/> is on one of this connection's VERIFIED domains.</summary>
    public bool CoversVerified(string? email)
    {
        var domain = SsoDomain.FromEmail(email);
        return domain is not null && _domains.Any(d => d.IsVerified && d.Domain == domain);
    }

    /// <summary>Replaces the SAML certificates (metadata refresh or manual rollover).</summary>
    public void ReplaceCertificates(IEnumerable<string> certificates)
    {
        var list = NormalizeCertificates(certificates);
        if (list.Count == 0)
            throw new InvalidSsoConnectionException("Sso.Connection.CertificateRequired");
        SamlIdpCertificates = list;
    }

    /// <summary>
    /// Raises the change event with the authority before and after the change, so every replica drops the cached
    /// discovery document of BOTH (an authority edit must not leave the old one cached).
    /// </summary>
    private void RaiseChanged(string? previousOidcAuthority = null) =>
        AddDomainEvent(new SsoConnectionChangedEvent(
            Id, TenantId, OidcAuthority,
            string.Equals(previousOidcAuthority, OidcAuthority, StringComparison.OrdinalIgnoreCase) ? null : previousOidcAuthority));

    private void Apply(SsoConnectionInput input, string? protectedClientSecret)
    {
        Name = Required(input.Name, NameMaxLength, "Name");
        EmailClaim = Optional(input.EmailClaim, ClaimMaxLength);
        GivenNameClaim = Optional(input.GivenNameClaim, ClaimMaxLength);
        FamilyNameClaim = Optional(input.FamilyNameClaim, ClaimMaxLength);
        JitProvisioning = input.JitProvisioning;
        EnforceSso = input.EnforceSso;
        TrustIdpMfa = input.TrustIdpMfa;

        var roles = (input.DefaultRoleIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (roles.Count > MaxDefaultRoles)
            throw new InvalidSsoConnectionException("Sso.Connection.TooManyDefaultRoles", MaxDefaultRoles);
        DefaultRoleIds = roles;

        if (Protocol == SsoProtocol.Oidc)
        {
            OidcAuthority = Optional(input.OidcAuthority, UrlMaxLength)?.TrimEnd('/');
            OidcClientId = Optional(input.OidcClientId, 512);
            OidcExtraScopes = Optional(input.OidcExtraScopes, 512);
            if (protectedClientSecret is not null)
                OidcClientSecretProtected = protectedClientSecret;
        }
        else
        {
            SamlIdpEntityId = Optional(input.SamlIdpEntityId, UrlMaxLength);
            SamlIdpSsoUrl = Optional(input.SamlIdpSsoUrl, UrlMaxLength);
            SamlIdpMetadataUrl = Optional(input.SamlIdpMetadataUrl, UrlMaxLength);
            SamlAllowIdpInitiated = input.SamlAllowIdpInitiated;
            if (input.SamlIdpCertificates is not null)
                SamlIdpCertificates = NormalizeCertificates(input.SamlIdpCertificates);
        }
    }

    private void EnsureComplete()
    {
        var missing = Protocol switch
        {
            SsoProtocol.Oidc when OidcAuthority is null => "the OIDC authority",
            SsoProtocol.Oidc when OidcClientId is null => "the OIDC client id",
            SsoProtocol.Oidc when OidcClientSecretProtected is null => "the OIDC client secret",
            SsoProtocol.Saml when SamlIdpEntityId is null => "the IdP entity id",
            SsoProtocol.Saml when SamlIdpSsoUrl is null => "the IdP single sign-on URL",
            SsoProtocol.Saml when SamlIdpCertificates.Count == 0 => "an IdP signing certificate",
            _ => null
        };

        if (missing is not null)
            throw new InvalidSsoConnectionException("Sso.Connection.Incomplete", missing);
    }

    private static List<string> NormalizeCertificates(IEnumerable<string> certificates)
    {
        var list = certificates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(SsoCertificates.ToBase64Der)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (list.Count > MaxCertificates)
            throw new InvalidSsoConnectionException("Sso.Connection.TooManyCertificates", MaxCertificates);
        return list;
    }

    private static string Required(string? value, int max, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new InvalidSsoConnectionException("Sso.Connection.FieldRequired", field);
        if (trimmed.Length > max)
            throw new InvalidSsoConnectionException("Sso.Connection.FieldTooLong", field, max);
        return trimmed;
    }

    private static string? Optional(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        if (trimmed.Length > max)
            throw new InvalidSsoConnectionException("Sso.Connection.ValueTooLong", max);
        return trimmed;
    }
}

/// <summary>An email domain claimed by a connection, proven with a DNS TXT record before it counts.</summary>
public class SsoDomain : Entity<Guid>
{
    public const string RecordPrefix = "_starterkit-sso";

    public Guid ConnectionId { get; private set; }
    public string Domain { get; private set; } = default!;
    public string VerificationToken { get; private set; } = default!;
    public DateTime? VerifiedAt { get; private set; }

    public bool IsVerified => VerifiedAt is not null;

    /// <summary>TXT record name to publish: <c>_starterkit-sso.{domain}</c>.</summary>
    public string RecordName => $"{RecordPrefix}.{Domain}";

    /// <summary>TXT record value to publish.</summary>
    public string RecordValue => $"sso-verification={VerificationToken}";

    private SsoDomain()
    {
    }

    internal static SsoDomain Create(Guid connectionId, string domain, string token) => new()
    {
        Id = Guid.CreateVersion7(),
        ConnectionId = connectionId,
        Domain = domain,
        VerificationToken = token
    };

    internal void MarkVerified(DateTime now) => VerifiedAt ??= now;

    public static string Normalize(string domain)
    {
        var value = domain?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;
        if (value.StartsWith('@'))
            value = value[1..];

        if (value.Length is 0 or > 253 || !value.Contains('.') ||
            Uri.CheckHostName(value) != UriHostNameType.Dns)
            throw new InvalidSsoConnectionException("Sso.Connection.InvalidDomain", domain);

        // Public email providers can never be claimed by one tenant.
        if (PublicEmailDomains.Contains(value))
            throw new InvalidSsoConnectionException("Sso.Connection.PublicEmailDomain", value);

        return value;
    }

    public static string? FromEmail(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        return at > 0 && at < email!.Length - 1 ? email[(at + 1)..].Trim().ToLowerInvariant() : null;
    }

    private static readonly HashSet<string> PublicEmailDomains = new(StringComparer.Ordinal)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "yahoo.com",
        "icloud.com", "me.com", "aol.com", "proton.me", "protonmail.com", "gmx.com", "yandex.com", "mail.com"
    };
}
