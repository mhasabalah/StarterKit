using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Identity.Contracts.Sso;
using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Identity.Services.Sso;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens.Saml2;

namespace Identity.Tests.Sso;

/// <summary>
/// The SAML service provider against real signed responses: ITfoxtec plays the IdP with a throwaway certificate,
/// our <see cref="SamlSsoClient"/> validates them exactly as in production.
/// </summary>
public sealed class SamlSsoClientTests : IDisposable
{
    private const string IdpEntityId = "https://idp.acme.example/saml";
    private const string IdpSsoUrl = "https://idp.acme.example/sso";

    private readonly X509Certificate2 _idpCertificate = SelfSigned("CN=acme-idp");
    private readonly X509Certificate2 _otherCertificate = SelfSigned("CN=attacker");
    private readonly SsoOptions _options = new() { PublicBaseUrl = "https://api.example.com" };
    private readonly IOneTimeUseGuard _guard = new DistributedCacheOneTimeUseGuard(
        new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions())));

    private SamlSsoClient Sut() => new(
        new SsoUrls(Microsoft.Extensions.Options.Options.Create(_options), new HttpContextAccessor(),
            Mock.Of<Microsoft.Extensions.Hosting.IHostEnvironment>()),
        _guard, Microsoft.Extensions.Options.Options.Create(_options), NullLogger<SamlSsoClient>.Instance);

    private SsoConnection Connection(bool allowIdpInitiated = false) => SsoConnection.Create(Guid.NewGuid(), new SsoConnectionInput
    {
        Name = "Acme",
        Protocol = SsoProtocol.Saml,
        SamlIdpEntityId = IdpEntityId,
        SamlIdpSsoUrl = IdpSsoUrl,
        SamlIdpCertificates = [Convert.ToBase64String(_idpCertificate.RawData)],
        SamlAllowIdpInitiated = allowIdpInitiated
    }, null);

    [Fact]
    public async Task A_signed_response_to_our_request_yields_the_identity()
    {
        var connection = Connection();
        var sut = Sut();
        sut.BuildAuthnRequest(connection, "state-1", out var requestId);

        var identity = await sut.ValidateResponseAsync(connection,
            SignedResponse(connection, requestId, email: "ada@acme.example", mfa: true), "s=state-1", requestId, default);

        identity.Subject.Should().Be("user-123");
        identity.Email.Should().Be("ada@acme.example");
        identity.GivenName.Should().Be("Ada");
        identity.MultiFactor.Should().BeTrue();
    }

    [Fact]
    public async Task A_response_signed_by_another_key_is_rejected()
    {
        var connection = Connection();
        var sut = Sut();
        sut.BuildAuthnRequest(connection, "s", out var requestId);

        var forged = SignedResponse(connection, requestId, signer: _otherCertificate);

        (await sut.Invoking(s => s.ValidateResponseAsync(connection, forged, null, requestId, default))
            .Should().ThrowAsync<SsoLoginFailedException>()).Which.Reason.Should().Be("saml_response_invalid");
    }

    [Fact]
    public async Task A_response_for_another_audience_is_rejected()
    {
        var connection = Connection();
        var sut = Sut();
        sut.BuildAuthnRequest(connection, "s", out var requestId);

        var wrongAudience = SignedResponse(connection, requestId, audience: "https://other-sp.example");

        await sut.Invoking(s => s.ValidateResponseAsync(connection, wrongAudience, null, requestId, default))
            .Should().ThrowAsync<SsoLoginFailedException>();
    }

    [Fact]
    public async Task A_response_to_a_different_request_is_rejected()
    {
        var connection = Connection();
        var sut = Sut();
        sut.BuildAuthnRequest(connection, "s", out var requestId);
        var response = SignedResponse(connection, "_someone-elses-request");

        (await sut.Invoking(s => s.ValidateResponseAsync(connection, response, null, requestId, default))
            .Should().ThrowAsync<SsoLoginFailedException>()).Which.Reason.Should().Be("in_response_to_mismatch");
    }

    [Fact]
    public async Task The_same_response_cannot_be_replayed()
    {
        var connection = Connection();
        var sut = Sut();
        sut.BuildAuthnRequest(connection, "s", out var requestId);
        var response = SignedResponse(connection, requestId);

        await sut.ValidateResponseAsync(connection, response, null, requestId, default);

        (await sut.Invoking(s => s.ValidateResponseAsync(connection, response, null, requestId, default))
            .Should().ThrowAsync<SsoLoginFailedException>()).Which.Reason.Should().Be("saml_response_replayed");
    }

    [Fact]
    public async Task Unsolicited_responses_need_explicit_opt_in()
    {
        var strict = Connection(allowIdpInitiated: false);
        var unsolicited = SignedResponse(strict, inResponseTo: null);

        (await Sut().Invoking(s => s.ValidateResponseAsync(strict, unsolicited, null, null, default))
            .Should().ThrowAsync<SsoLoginFailedException>()).Which.Reason.Should().Be("unsolicited_response");

        var relaxed = Connection(allowIdpInitiated: true);
        var identity = await Sut().ValidateResponseAsync(relaxed, SignedResponse(relaxed, inResponseTo: null), null, null, default);
        identity.Subject.Should().Be("user-123");
    }

    [Fact]
    public void The_authn_request_redirects_to_the_idp_with_relay_state()
    {
        var redirect = Sut().BuildAuthnRequest(Connection(), "state-xyz", out var requestId);

        redirect.Url.Should().StartWith(IdpSsoUrl + "?SAMLRequest=");
        SamlSsoClient.ReadRelayState(System.Web.HttpUtility.ParseQueryString(new Uri(redirect.Url).Query)["RelayState"])
            .Should().Be("state-xyz");
        requestId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Sp_metadata_names_the_connection_entity_id_and_acs()
    {
        var connection = Connection();

        var xml = Sut().BuildMetadataXml(connection);

        xml.Should().Contain($"entityID=\"https://api.example.com/api/auth/sso/saml/{connection.Id}\"");
        xml.Should().Contain($"https://api.example.com/api/auth/sso/saml/{connection.Id}/acs");
    }

    // ---- the "IdP" ---------------------------------------------------------------------------------------------

    private string SignedResponse(
        SsoConnection connection, string? inResponseTo, string email = "ada@acme.example", bool mfa = false,
        X509Certificate2? signer = null, string? audience = null)
    {
        var spEntityId = $"https://api.example.com/api/auth/sso/saml/{connection.Id}";
        var config = new Saml2Configuration { Issuer = IdpEntityId, SigningCertificate = signer ?? _idpCertificate };

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-123"),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.GivenName, "Ada")
        };
        if (mfa)
            claims.Add(new Claim("http://schemas.microsoft.com/claims/authnmethodsreferences",
                "http://schemas.microsoft.com/claims/multipleauthn"));

        var response = new Saml2AuthnResponse(config)
        {
            Status = Saml2StatusCodes.Success,
            Destination = new Uri(spEntityId + "/acs"),
            NameId = new Saml2NameIdentifier("user-123", NameIdentifierFormats.Persistent),
            ClaimsIdentity = new ClaimsIdentity(claims, "saml")
        };
        if (inResponseTo is not null)
            response.InResponseTo = new Saml2Id(inResponseTo);

        response.CreateSecurityToken(audience ?? spEntityId, subjectConfirmationLifetime: 5, issuedTokenLifetime: 60);

        var binding = new Saml2PostBinding();
        binding.Bind(response);
        var match = Regex.Match(binding.PostContent, "name=\"SAMLResponse\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue();
        return System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static X509Certificate2 SelfSigned(string subject)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        // Re-import so the private key is usable for XML signing on every OS.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12, "p"), "p",
            X509KeyStorageFlags.Exportable);
    }

    public void Dispose()
    {
        _idpCertificate.Dispose();
        _otherCertificate.Dispose();
    }
}
