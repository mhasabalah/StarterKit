using System.IdentityModel.Tokens.Jwt;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using Identity.Contracts.Sso;
using Identity.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens.Saml2;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// SAML 2.0 over real HTTP: SP-initiated start (HTTP-Redirect AuthnRequest + browser-binding cookie) → a response
/// signed by the "IdP" (ITfoxtec with a throwaway certificate) POSTed to the ACS → one-time code → session. Plus the
/// edges that matter at the HTTP boundary: a forged signature and a response posted from another browser.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class SamlSsoE2ETests(E2ETestFixture fixture) : IDisposable
{
    private const string Admin = "/api/v1/tenant/sso";
    private const string Sso = "/api/auth/sso";
    private const string ReturnUrl = "http://localhost:3000/auth/sso";
    private const string IdpEntityId = "https://idp.saml-e2e.example/saml";
    private const string IdpSsoUrl = "https://idp.saml-e2e.example/sso";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SamlTestIdp _idp = new(IdpEntityId, IdpSsoUrl);
    private readonly X509Certificate2 _attackerCertificate = SamlTestIdp.SelfSigned("CN=attacker");
    private readonly string _domain = $"saml-{Guid.NewGuid():N}.example";

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record LoginData(string? AccessToken, string? RefreshToken, bool RequiresTwoFactor);

    [Fact]
    public async Task Full_saml_sign_in_provisions_the_user_and_returns_a_session()
    {
        var connection = await ActiveSamlConnectionAsync();
        var browser = Browser();
        var (requestId, relayState) = await StartAsync(browser, connection.Id);
        var email = $"grace@{_domain}";

        var acs = await PostResponseAsync(browser, connection,
            _idp.SignedResponse(connection, requestId, "saml-user-1", email), relayState);

        acs.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = acs.Headers.Location!;
        location.ToString().Should().StartWith(ReturnUrl);
        var code = System.Web.HttpUtility.ParseQueryString(location.Query)["sso_code"];
        code.Should().NotBeNull($"the ACS sent the browser to {location}");

        var exchange = await fixture.CreateAnonymousClient().PostAsJsonAsync($"{Sso}/exchange", new { code });
        exchange.StatusCode.Should().Be(HttpStatusCode.OK, await exchange.Content.ReadAsStringAsync());
        var login = JsonSerializer.Deserialize<Envelope<LoginData>>(await exchange.Content.ReadAsStringAsync(), Json)!.Data!;
        login.AccessToken.Should().NotBeNullOrEmpty();
        var userId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken).Subject);

        await fixture.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationIdentityDbContext>();
            (await db.Users.SingleAsync(u => u.Id == userId)).Email.Should().Be(email);
            (await db.UserLogins.AnyAsync(l => l.UserId == userId && l.ProviderKey == "saml-user-1")).Should().BeTrue();
        });

        // The same response cannot be replayed.
        var replay = await PostResponseAsync(browser, connection,
            _idp.SignedResponse(connection, requestId, "saml-user-1", email), relayState);
        replay.Headers.Location!.ToString().Should().Contain("sso_error=");
    }

    [Fact]
    public async Task A_response_signed_by_another_key_is_refused()
    {
        var connection = await ActiveSamlConnectionAsync();
        var browser = Browser();
        var (requestId, relayState) = await StartAsync(browser, connection.Id);

        var acs = await PostResponseAsync(browser, connection,
            _idp.SignedResponse(connection, requestId, "saml-user-2", $"eve@{_domain}", _attackerCertificate), relayState);

        acs.StatusCode.Should().Be(HttpStatusCode.Redirect);
        acs.Headers.Location!.ToString().Should().StartWith(ReturnUrl).And.Contain("sso_error=").And.NotContain("sso_code");
    }

    [Fact]
    public async Task A_response_posted_from_another_browser_is_refused()
    {
        var connection = await ActiveSamlConnectionAsync();
        var (requestId, relayState) = await StartAsync(Browser(), connection.Id);

        var acs = await PostResponseAsync(Browser(), connection,
            _idp.SignedResponse(connection, requestId, "saml-user-3", $"mallory@{_domain}"), relayState);

        acs.StatusCode.Should().Be(HttpStatusCode.Redirect);
        acs.Headers.Location!.ToString().Should().Contain("sso_error=state_invalid");
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private HttpClient AdminClient()
    {
        var client = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");
        return client;
    }

    private HttpClient Browser() =>
        fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<SsoConnectionDto> ActiveSamlConnectionAsync()
    {
        var created = await AdminClient().PostAsJsonAsync($"{Admin}/connections", new
        {
            name = "Acme ADFS",
            protocol = "Saml",
            samlIdpEntityId = IdpEntityId,
            samlIdpSsoUrl = IdpSsoUrl,
            samlIdpCertificates = new[] { _idp.CertificateBase64 },
            jitProvisioning = true
        });
        var text = await created.Content.ReadAsStringAsync();
        created.StatusCode.Should().Be(HttpStatusCode.Created, text);
        var connection = JsonSerializer.Deserialize<Envelope<SsoConnectionDto>>(text, Json)!.Data!;

        (await AdminClient().PostAsJsonAsync($"{Admin}/connections/{connection.Id}/domains", new { domain = _domain }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationIdentityDbContext>();
            var entity = await db.SsoConnections.Include(c => c.Domains).FirstAsync(c => c.Id == connection.Id);
            entity.MarkDomainVerified(_domain, DateTime.UtcNow);
            await db.SaveChangesAsync();
        });
        var activated = await AdminClient().PostAsJsonAsync($"{Admin}/connections/{connection.Id}/activate", new { });
        activated.StatusCode.Should().Be(HttpStatusCode.OK, await activated.Content.ReadAsStringAsync());
        return connection;
    }

    private Task<(string RequestId, string RelayState)> StartAsync(HttpClient browser, Guid connectionId) =>
        _idp.StartAsync(browser, connectionId, ReturnUrl);

    private static Task<HttpResponseMessage> PostResponseAsync(
        HttpClient browser, SsoConnectionDto connection, string samlResponse, string relayState) =>
        SamlTestIdp.PostResponseAsync(browser, connection.Id, samlResponse, relayState);

    public void Dispose()
    {
        _idp.Dispose();
        _attackerCertificate.Dispose();
    }
}
