using System.IO.Compression;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Identity.Contracts.Sso;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.IdentityModel.Tokens.Saml2;

namespace Api.E2ETests.Identity;

/// <summary>
/// A fake SAML identity provider for E2E tests: a throwaway signing certificate plus the browser-side steps of the
/// SP-initiated flow (decode the AuthnRequest of the start redirect, sign a Response, POST it to the ACS).
/// </summary>
public sealed class SamlTestIdp(string entityId, string ssoUrl, string subjectName = "CN=saml-e2e-idp") : IDisposable
{
    public const string Sso = "/api/auth/sso";

    public string EntityId { get; } = entityId;

    public string SsoUrl { get; } = ssoUrl;

    public X509Certificate2 Certificate { get; } = SelfSigned(subjectName);

    /// <summary>Base64 DER of the signing certificate, as a connection stores it.</summary>
    public string CertificateBase64 => Convert.ToBase64String(Certificate.RawData);

    /// <summary>GET start; returns the AuthnRequest id (decoded from the redirect) and the RelayState to echo back.</summary>
    public async Task<(string RequestId, string RelayState)> StartAsync(HttpClient browser, Guid connectionId, string returnUrl)
    {
        var start = await browser.GetAsync($"{Sso}/{connectionId}/start?returnUrl={Uri.EscapeDataString(returnUrl)}");
        start.StatusCode.Should().Be(HttpStatusCode.Redirect, await start.Content.ReadAsStringAsync());
        var location = start.Headers.Location!;
        location.ToString().Should().StartWith(SsoUrl);

        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        var xml = Inflate(query["SAMLRequest"]!);
        var id = Regex.Match(xml, "\\sID=\"([^\"]+)\"");
        id.Success.Should().BeTrue(xml);
        return (id.Groups[1].Value, query["RelayState"]!);
    }

    public static Task<HttpResponseMessage> PostResponseAsync(
        HttpClient browser, Guid connectionId, string samlResponse, string relayState) =>
        browser.PostAsync($"{Sso}/saml/{connectionId}/acs", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["SAMLResponse"] = samlResponse,
            ["RelayState"] = relayState
        }));

    /// <summary>What the IdP posts back: a signed Response for our entity id and ACS, in reply to our request.</summary>
    public string SignedResponse(
        SsoConnectionDto connection, string inResponseTo, string subject, string email, X509Certificate2? signer = null)
    {
        var spEntityId = connection.ServiceProvider.SamlEntityId!;
        var config = new Saml2Configuration { Issuer = EntityId, SigningCertificate = signer ?? Certificate };
        var response = new Saml2AuthnResponse(config)
        {
            Status = Saml2StatusCodes.Success,
            Destination = new Uri(connection.ServiceProvider.SamlAcsUrl!),
            InResponseTo = new Saml2Id(inResponseTo),
            NameId = new Saml2NameIdentifier(subject, NameIdentifierFormats.Persistent),
            ClaimsIdentity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, subject),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.GivenName, "Grace")
            ], "saml")
        };
        response.CreateSecurityToken(spEntityId, subjectConfirmationLifetime: 5, issuedTokenLifetime: 60);

        var binding = new Saml2PostBinding();
        binding.Bind(response);
        var match = Regex.Match(binding.PostContent, "name=\"SAMLResponse\" value=\"([^\"]+)\"");
        match.Success.Should().BeTrue();
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    /// <summary>The IdP metadata document a metadata URL would serve (entity id, SSO URL, signing certificates).</summary>
    public string MetadataXml(string? entityId = null, params X509Certificate2[] certificates)
    {
        var keys = (certificates.Length == 0 ? [Certificate] : certificates)
            .Select(c => $"<md:KeyDescriptor use=\"signing\"><ds:KeyInfo xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">" +
                         $"<ds:X509Data><ds:X509Certificate>{Convert.ToBase64String(c.RawData)}</ds:X509Certificate></ds:X509Data>" +
                         "</ds:KeyInfo></md:KeyDescriptor>");
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{entityId ?? EntityId}">
                  <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                    {string.Concat(keys)}
                    <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect" Location="{SsoUrl}"/>
                  </md:IDPSSODescriptor>
                </md:EntityDescriptor>
                """;
    }

    /// <summary>HTTP-Redirect binding: base64 of raw DEFLATE.</summary>
    public static string Inflate(string value)
    {
        using var input = new MemoryStream(Convert.FromBase64String(value));
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(deflate, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static X509Certificate2 SelfSigned(string subject, TimeSpan? validFor = null)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.Add(validFor ?? TimeSpan.FromDays(365)));
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12, "p"), "p",
            X509KeyStorageFlags.Exportable);
    }

    public void Dispose() => Certificate.Dispose();
}
