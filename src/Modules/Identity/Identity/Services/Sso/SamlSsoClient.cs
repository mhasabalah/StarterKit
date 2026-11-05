using System.Collections.Specialized;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Identity.Contracts.Sso;
using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Microsoft.Extensions.Options;
using Saml2HttpRequest = ITfoxtec.Identity.Saml2.Http.HttpRequest;

namespace Identity.Services.Sso;

/// <summary>
/// SAML 2.0 service provider for one tenant connection (ITfoxtec.Identity.Saml2, BSD-3-Clause):
/// SP-initiated AuthnRequest over HTTP-Redirect, Response over HTTP-POST to the per-connection ACS.
/// <list type="bullet">
/// <item>Signature: the Response or Assertion must be signed by one of the PINNED IdP certificates (no chain
/// building: the certificate itself is the trust anchor, as in every SAML deployment).</item>
/// <item>Audience = this connection's SP entity id; Issuer = the configured IdP entity id; time conditions with
/// bounded skew.</item>
/// <item>InResponseTo must match the AuthnRequest we issued (unless IdP-initiated was explicitly allowed), and each
/// Response id is accepted once (replay guard shared by all instances).</item>
/// </list>
/// </summary>
public sealed class SamlSsoClient(
    SsoUrls urls,
    IOneTimeUseGuard replayGuard,
    IOptions<SsoOptions> options,
    ILogger<SamlSsoClient> logger)
{
    private static readonly string[] EmailAttributes =
    [
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
        "email", "mail", "Email", "emailAddress", "urn:oid:0.9.2342.19200300.100.1.3",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn"
    ];

    private static readonly string[] GivenNameAttributes =
    [
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname", "firstName", "givenName", "given_name",
        "urn:oid:2.5.4.42"
    ];

    private static readonly string[] FamilyNameAttributes =
    [
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/surname", "lastName", "sn", "surname", "family_name",
        "urn:oid:2.5.4.4"
    ];

    private static readonly string[] AuthnMethodAttributes =
    [
        "http://schemas.microsoft.com/claims/authnmethodsreferences",
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/authenticationmethod",
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/authenticationmethod"
    ];

    public SsoRedirect BuildAuthnRequest(SsoConnection connection, string relayState, out string requestId)
    {
        var configuration = Configuration(connection);
        var request = new Saml2AuthnRequest(configuration)
        {
            AssertionConsumerServiceUrl = new Uri(urls.SamlAcsUrl(connection.Id)),
            NameIdPolicy = new NameIdPolicy { AllowCreate = true }
        };

        var binding = new Saml2RedirectBinding();
        binding.SetRelayStateQuery(new Dictionary<string, string> { ["s"] = relayState });
        binding.Bind(request);

        requestId = request.IdAsString;
        return new SsoRedirect(binding.RedirectLocation.OriginalString);
    }

    /// <summary>Reads the RelayState from the posted form (before validation, to find our state record).</summary>
    public static string? ReadRelayState(string? relayState)
    {
        if (string.IsNullOrEmpty(relayState))
            return null;

        // Saml2RedirectBinding.SetRelayStateQuery encodes "s=<id>" as a query string.
        var parsed = System.Web.HttpUtility.ParseQueryString(relayState);
        return parsed["s"];
    }

    public async Task<SsoIdentity> ValidateResponseAsync(
        SsoConnection connection, string samlResponse, string? relayState, string? expectedRequestId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(samlResponse) || samlResponse.Length > 512 * 1024)
            throw new SsoLoginFailedException("saml_response_missing");

        var configuration = Configuration(connection);
        var response = new Saml2AuthnResponse(configuration);
        var http = new Saml2HttpRequest
        {
            Method = "POST",
            Form = new NameValueCollection { ["SAMLResponse"] = samlResponse, ["RelayState"] = relayState ?? string.Empty }
        };

        try
        {
            new Saml2PostBinding().Unbind(http, response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bad signature, wrong audience/issuer, expired conditions, malformed XML: one generic failure outward.
            logger.LogWarning(ex, "SAML response rejected for connection {ConnectionId}", connection.Id);
            throw new SsoLoginFailedException("saml_response_invalid");
        }

        if (response.Status != Saml2StatusCodes.Success)
        {
            logger.LogWarning("SAML IdP returned status {Status} for connection {ConnectionId}", response.Status, connection.Id);
            throw new SsoLoginFailedException("idp_status_" + response.Status.ToString().Split(':').Last());
        }

        // InResponseToAsString throws when the attribute is absent (IdP-initiated); read the raw id instead.
        var inResponseTo = response.InResponseTo?.Value;
        if (expectedRequestId is not null)
        {
            if (!string.Equals(inResponseTo, expectedRequestId, StringComparison.Ordinal))
                throw new SsoLoginFailedException("in_response_to_mismatch");
        }
        else if (!connection.SamlAllowIdpInitiated || !string.IsNullOrEmpty(inResponseTo))
        {
            throw new SsoLoginFailedException("unsolicited_response");
        }

        var window = TimeSpan.FromMinutes(Math.Max(5, options.Value.LoginTimeoutMinutes)) + TimeSpan.FromHours(1);
        if (!await replayGuard.TryUseAsync($"saml:{connection.Id:N}:{response.IdAsString}", window, ct))
            throw new SsoLoginFailedException("saml_response_replayed");

        var claims = response.ClaimsIdentity.Claims
            .GroupBy(c => c.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray(), StringComparer.Ordinal);

        var nameId = First(claims, ClaimTypes.NameIdentifier) ?? throw new SsoLoginFailedException("missing_name_id");
        var email = First(claims, connection.EmailClaim) ?? FirstOf(claims, EmailAttributes)
                    ?? (nameId.Contains('@') ? nameId : null);

        return new SsoIdentity(
            nameId,
            email,
            EmailVerified: true,
            First(claims, connection.GivenNameClaim) ?? FirstOf(claims, GivenNameAttributes),
            First(claims, connection.FamilyNameClaim) ?? FirstOf(claims, FamilyNameAttributes),
            AssertsMfa(claims),
            claims.ToDictionary(c => c.Key, c => string.Join(",", c.Value), StringComparer.Ordinal));
    }

    /// <summary>This connection's SP metadata (what the IdP admin uploads).</summary>
    public string BuildMetadataXml(SsoConnection connection)
    {
        var configuration = Configuration(connection);
        var descriptor = new EntityDescriptor(configuration)
        {
            ValidUntil = 365,
            SPSsoDescriptor = new SPSsoDescriptor
            {
                AuthnRequestsSigned = false,
                WantAssertionsSigned = true,
                NameIDFormats = [NameIdentifierFormats.Email, NameIdentifierFormats.Persistent],
                AssertionConsumerServices =
                [
                    new AssertionConsumerService
                    {
                        Binding = ProtocolBindings.HttpPost,
                        Location = new Uri(urls.SamlAcsUrl(connection.Id)),
                        IsDefault = true,
                        Index = 0
                    }
                ]
            }
        };
        return new Saml2Metadata(descriptor).CreateMetadata().ToXml();
    }

    /// <summary>Parses IdP metadata XML: entity id, HTTP-Redirect SSO URL and signing certificates.</summary>
    public static (string EntityId, string? SsoUrl, IReadOnlyList<string> Certificates) ParseIdpMetadata(string xml)
    {
        var descriptor = new EntityDescriptor().ReadIdPSsoDescriptor(xml);
        var sso = descriptor.IdPSsoDescriptor?.SingleSignOnServices?
            .FirstOrDefault(s => s.Binding?.OriginalString == ProtocolBindings.HttpRedirect.OriginalString)
            ?.Location?.OriginalString;
        var certificates = descriptor.IdPSsoDescriptor?.SigningCertificates?
            .Select(c => Convert.ToBase64String(c.RawData)).ToList() ?? [];
        return (descriptor.EntityId, sso, certificates);
    }

    private Saml2Configuration Configuration(SsoConnection connection)
    {
        if (!SsoHttp.IsAcceptableUrl(connection.SamlIdpSsoUrl, options.Value.AllowInsecureIdentityProviders))
            throw new SsoLoginFailedException("The IdP single sign-on URL is not an acceptable URL.");

        var configuration = new Saml2Configuration
        {
            Issuer = urls.SamlEntityId(connection.Id),
            SingleSignOnDestination = new Uri(connection.SamlIdpSsoUrl!),
            AllowedIssuer = connection.SamlIdpEntityId,
            // The pinned certificates ARE the trust anchors (standard SAML practice): no chain or revocation lookup.
            CertificateValidationMode = X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck,
            AudienceRestricted = true
        };
        configuration.AllowedAudienceUris.Add(configuration.Issuer);
        foreach (var certificate in connection.SamlIdpCertificates)
            configuration.SignatureValidationCertificates.Add(
                X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificate)));

        return configuration;
    }

    private static bool AssertsMfa(Dictionary<string, string[]> claims) =>
        AuthnMethodAttributes.Any(a => claims.TryGetValue(a, out var values) && values.Any(v =>
            v.Contains("multipleauthn", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("MultiFactor", StringComparison.OrdinalIgnoreCase) ||
            v.Contains("TwoFactor", StringComparison.OrdinalIgnoreCase) ||
            v.EndsWith(":mfa", StringComparison.OrdinalIgnoreCase)));

    private static string? First(Dictionary<string, string[]> claims, string? type) =>
        type is not null && claims.TryGetValue(type, out var values)
            ? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
            : null;

    private static string? FirstOf(Dictionary<string, string[]> claims, IEnumerable<string> types) =>
        types.Select(t => First(claims, t)).FirstOrDefault(v => v is not null);
}
