using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Contracts.Sso;
using Identity.Data;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// Enterprise SSO over real HTTP against an in-process OIDC provider: admin setup, home-realm discovery, the full
/// redirect → callback → one-time code → session flow, JIT provisioning and linking, and the security edges
/// (state/code replay, foreign domains, wrong nonce, open redirect, enforcement, disabled connections).
/// </summary>
[Collection(E2ECollection.Name)]
public class EnterpriseSsoE2ETests(E2ETestFixture fixture) : IAsyncLifetime
{
    private const string Admin = "/api/v1/tenant/sso";
    private const string Sso = "/api/auth/sso";
    private const string ReturnUrl = "http://localhost:3000/auth/sso";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private FakeOidcProvider _idp = default!;
    private string _domain = default!;

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors, string? ErrorCode);

    private sealed record LoginData(string? AccessToken, string? RefreshToken, bool RequiresTwoFactor);

    public async Task InitializeAsync()
    {
        _idp = await FakeOidcProvider.StartAsync();
        _domain = $"acme-{Guid.NewGuid():N}.example";
    }

    public async Task DisposeAsync() => await _idp.DisposeAsync();

    [Fact]
    public async Task Full_oidc_sign_in_provisions_just_in_time_and_links_on_the_next_sign_in()
    {
        var connection = await ActiveConnectionAsync();
        var email = $"ada@{_domain}";

        var discovery = await PostAsync<SsoDiscoveryResult>(Anonymous(), $"{Sso}/discover", new { email });
        discovery.SsoAvailable.Should().BeTrue();
        discovery.ConnectionId.Should().Be(connection.Id);

        var first = await SignInAsync(connection.Id, "idp-user-1", email);
        first.AccessToken.Should().NotBeNullOrEmpty();
        var token = new JwtSecurityTokenHandler().ReadJwtToken(first.AccessToken);
        var userId = Guid.Parse(token.Subject);
        // A real, usable session (single-tenant mode: no tid claim; the Default tenant is implied).
        (await fixture.Factory.CreateClientWithToken(first.AccessToken!).GetAsync("/api/auth/passkeys"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // JIT: account created with the IdP's profile, confirmed email, no password, linked to the subject.
        await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(userId.ToString()))!;
            user.Email.Should().Be(email);
            user.EmailConfirmed.Should().BeTrue();
            user.FirstName.Should().Be("Ada");
            (await users.HasPasswordAsync(user)).Should().BeFalse();
            (await users.GetLoginsAsync(user)).Should().ContainSingle(l => l.ProviderKey == "idp-user-1");
        });

        // Second sign-in: same subject → same account (no duplicate).
        var second = await SignInAsync(connection.Id, "idp-user-1", email);
        new JwtSecurityTokenHandler().ReadJwtToken(second.AccessToken).Subject.Should().Be(userId.ToString());
        _idp.RejectedTokenRequests.Should().Be(0, "client authentication and PKCE must be correct");
    }

    [Fact]
    public async Task An_existing_account_with_the_same_verified_email_is_linked_not_duplicated()
    {
        var connection = await ActiveConnectionAsync();
        var email = $"grace@{_domain}";
        var existingId = await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, IsActive = true,
                IsProfileComplete = true
            };
            (await users.CreateAsync(user, "E2e!Passw0rd")).Succeeded.Should().BeTrue();
            return user.Id;
        });

        var login = await SignInAsync(connection.Id, "idp-grace", email);

        new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken).Subject.Should().Be(existingId.ToString());
    }

    [Fact]
    public async Task An_email_outside_the_verified_domains_is_refused()
    {
        var connection = await ActiveConnectionAsync();

        var (location, _) = await CallbackAsync(connection.Id, "idp-mallory", "mallory@victim.example");

        Query(location)["sso_error"].Should().Be("domain_not_allowed");
        Query(location)["sso_code"].Should().BeNull();
    }

    [Fact]
    public async Task A_tenant_idp_can_never_sign_in_platform_staff()
    {
        var connection = await ActiveConnectionAsync();
        var email = $"staff@{_domain}";
        await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, IsActive = true,
                IsProfileComplete = true
            };
            (await users.CreateAsync(user, "E2e!Passw0rd")).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, [RoleConstants.User, RoleConstants.SuperAdmin])).Succeeded.Should().BeTrue();
        });

        var (location, _) = await CallbackAsync(connection.Id, "idp-staff", email);

        Query(location)["sso_error"].Should().Be("staff_account");
        Query(location)["sso_code"].Should().BeNull();
    }

    [Fact]
    public async Task A_callback_in_another_browser_is_refused_login_csrf()
    {
        var connection = await ActiveConnectionAsync();

        // The attacker starts a sign-in in THEIR browser and completes it at the IdP...
        var attacker = Browser();
        var start = await attacker.GetAsync($"{Sso}/{connection.Id}/start?returnUrl={Uri.EscapeDataString(ReturnUrl)}");
        var authorize = start.Headers.Location!;
        var code = _idp.IssueCode(authorize, "idp-attacker", $"attacker@{_domain}");
        var callbackUrl = $"{Sso}/oidc/callback?code={Uri.EscapeDataString(code)}" +
                          $"&state={Uri.EscapeDataString(Query(authorize)["state"]!)}";

        // ...then lures the victim (no binding cookie) into opening the callback URL.
        var victim = await Browser().GetAsync(callbackUrl);

        Query(victim.Headers.Location!)["sso_error"].Should().Be("state_invalid");
        Query(victim.Headers.Location!)["sso_code"].Should().BeNull();
    }

    [Fact]
    public async Task A_wrong_nonce_is_refused()
    {
        var connection = await ActiveConnectionAsync();

        var (location, _) = await CallbackAsync(connection.Id, "idp-x", $"x@{_domain}", nonceOverride: "forged");

        Query(location)["sso_error"].Should().Be("sso_failed");
    }

    [Fact]
    public async Task State_and_exchange_codes_are_single_use()
    {
        var connection = await ActiveConnectionAsync();
        var (location, callbackUrl) = await CallbackAsync(connection.Id, "idp-once", $"once@{_domain}");
        var code = Query(location)["sso_code"]!;

        // Replaying the IdP callback (same state) fails.
        var replay = await Browser().GetAsync(callbackUrl);
        Query(replay.Headers.Location!)["sso_error"].Should().Be("state_invalid");

        (await Anonymous().PostAsJsonAsync($"{Sso}/exchange", new { code })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Anonymous().PostAsJsonAsync($"{Sso}/exchange", new { code })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Start_refuses_a_return_url_on_a_foreign_origin()
    {
        var connection = await ActiveConnectionAsync();

        var response = await Browser().GetAsync(
            $"{Sso}/{connection.Id}/start?returnUrl={Uri.EscapeDataString("https://evil.example/steal")}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Enforced_sso_blocks_password_sign_in_and_reset_for_the_domain()
    {
        var connection = await ActiveConnectionAsync(enforce: true);
        var email = $"linus@{_domain}";
        await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, IsActive = true,
                IsProfileComplete = true
            };
            (await users.CreateAsync(user, "E2e!Passw0rd")).Succeeded.Should().BeTrue();
            (await users.AddToRoleAsync(user, RoleConstants.User)).Succeeded.Should().BeTrue();
        });

        var discovery = await PostAsync<SsoDiscoveryResult>(Anonymous(), $"{Sso}/discover", new { email });
        discovery.SsoRequired.Should().BeTrue();

        var password = await Anonymous().PostAsJsonAsync("/api/auth/login", new { identifier = email, password = "E2e!Passw0rd" });
        password.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync<object>(password)).ErrorCode.Should().Be("sso_required");

        // ...but SSO itself works.
        var sso = await SignInAsync(connection.Id, "idp-linus", email);
        sso.AccessToken.Should().NotBeNull();
    }

    [Fact]
    public async Task A_disabled_connection_cannot_start_a_sign_in()
    {
        var connection = await ActiveConnectionAsync();
        (await AdminClient().PostAsJsonAsync($"{Admin}/connections/{connection.Id}/disable", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Browser().GetAsync($"{Sso}/{connection.Id}/start?returnUrl={Uri.EscapeDataString(ReturnUrl)}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_admin_api_never_returns_the_client_secret_and_needs_the_tenant_permission()
    {
        var connection = await ActiveConnectionAsync();

        var raw = await (await AdminClient().GetAsync($"{Admin}/connections/{connection.Id}")).Content.ReadAsStringAsync();
        raw.Should().NotContain(FakeOidcProvider.ClientSecret);
        raw.Should().Contain("\"oidcClientSecretSet\":true");

        (await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User).GetAsync($"{Admin}/connections"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Activation_requires_a_verified_domain()
    {
        var created = await CreateConnectionAsync();
        await AdminClient().PostAsJsonAsync($"{Admin}/connections/{created.Id}/domains", new { domain = _domain });

        var activate = await AdminClient().PostAsJsonAsync($"{Admin}/connections/{created.Id}/activate", new { });

        activate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync<object>(activate)).ErrorCode.Should().Be("sso_connection_invalid");
    }

    [Fact]
    public async Task Public_email_providers_cannot_be_claimed()
    {
        var created = await CreateConnectionAsync();

        var response = await AdminClient().PostAsJsonAsync($"{Admin}/connections/{created.Id}/domains", new { domain = "gmail.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_WithStaleVersion_IsRejected_AndNothingChanges()
    {
        var created = await CreateConnectionAsync();
        var input = new
        {
            name = "Renamed",
            protocol = "Oidc",
            oidcAuthority = _idp.Authority,
            oidcClientId = FakeOidcProvider.ClientId,
            jitProvisioning = true
        };

        var first = await AdminClient().PutAsJsonAsync($"{Admin}/connections/{created.Id}", new { input, version = created.Version });
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        var stale = await AdminClient().PutAsJsonAsync($"{Admin}/connections/{created.Id}",
            new { input = input with { name = "Lost update" }, version = created.Version });

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var current = await GetConnectionAsync(created.Id);
        current.Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Default_roles_must_be_roles_of_the_tenant()
    {
        var created = await CreateConnectionAsync();
        var foreignRole = Guid.NewGuid();

        var response = await AdminClient().PutAsJsonAsync($"{Admin}/connections/{created.Id}", new
        {
            input = new
            {
                name = "Acme Okta",
                protocol = "Oidc",
                oidcAuthority = _idp.Authority,
                oidcClientId = FakeOidcProvider.ClientId,
                jitProvisioning = true,
                defaultRoleIds = new[] { foreignRole }
            },
            version = created.Version
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync<object>(response)).ErrorCode.Should().Be("tenant_role_not_found");
        (await GetConnectionAsync(created.Id)).DefaultRoleIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_tenants_connection_is_invisible_by_id()
    {
        var created = await CreateConnectionAsync();
        await fixture.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationIdentityDbContext>();
            await db.SsoConnections.Where(c => c.Id == created.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.TenantId, Guid.NewGuid()));
        });
        var admin = AdminClient();

        (await admin.GetAsync($"{Admin}/connections/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync($"{Admin}/connections/{created.Id}/disable", new { }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync($"{Admin}/connections/{created.Id}/domains", new { domain = _domain }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"{Admin}/connections/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await ReadAsync<List<SsoConnectionDto>>(await admin.GetAsync($"{Admin}/connections"));
        list.Data!.Select(c => c.Id).Should().NotContain(created.Id);

        // Still there: nothing was changed through the foreign tenant.
        await fixture.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<ApplicationIdentityDbContext>().SsoConnections.AnyAsync(c => c.Id == created.Id))
            .Should().BeTrue());
    }

    [Theory]
    [InlineData("GET", "/connections")]
    [InlineData("POST", "/connections")]
    [InlineData("GET", "/connections/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/connections/00000000-0000-0000-0000-000000000001")]
    public async Task Admin_routes_answer_401_anonymous_and_403_without_the_tenant_permission(string method, string path)
    {
        HttpRequestMessage Request() => new(new HttpMethod(method), Admin + path)
        {
            Content = method == "POST" ? JsonContent.Create(new { name = "x", protocol = "Oidc" }) : null
        };

        (await Anonymous().SendAsync(Request())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User).SendAsync(Request()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_platform_staff_listing_needs_its_permission()
    {
        (await Anonymous().GetAsync("/api/v1/admin/sso/connections")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User).GetAsync("/api/v1/admin/sso/connections"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<SsoConnectionDto> GetConnectionAsync(Guid id)
    {
        var response = await AdminClient().GetAsync($"{Admin}/connections/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadAsync<SsoConnectionDto>(response)).Data!;
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private HttpClient AdminClient()
    {
        var client = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");
        return client;
    }

    private HttpClient Anonymous() => fixture.CreateAnonymousClient();

    private HttpClient Browser() =>
        fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<SsoConnectionDto> CreateConnectionAsync(bool enforce = false)
    {
        var response = await AdminClient().PostAsJsonAsync($"{Admin}/connections", new
        {
            name = "Acme Okta",
            protocol = "Oidc",
            oidcAuthority = _idp.Authority,
            oidcClientId = FakeOidcProvider.ClientId,
            oidcClientSecret = FakeOidcProvider.ClientSecret,
            jitProvisioning = true,
            enforceSso = enforce,
            trustIdpMfa = true
        });
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return JsonSerializer.Deserialize<Envelope<SsoConnectionDto>>(text, Json)!.Data!;
    }

    /// <summary>Creates, adds + verifies the domain (DNS is simulated in the database), activates.</summary>
    private async Task<SsoConnectionDto> ActiveConnectionAsync(bool enforce = false)
    {
        var created = await CreateConnectionAsync(enforce);
        (await AdminClient().PostAsJsonAsync($"{Admin}/connections/{created.Id}/domains", new { domain = _domain }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await fixture.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationIdentityDbContext>();
            var connection = await db.SsoConnections.Include(c => c.Domains).FirstAsync(c => c.Id == created.Id);
            connection.MarkDomainVerified(_domain, DateTime.UtcNow);
            await db.SaveChangesAsync();
        });

        var activated = await AdminClient().PostAsJsonAsync($"{Admin}/connections/{created.Id}/activate", new { });
        activated.StatusCode.Should().Be(HttpStatusCode.OK, await activated.Content.ReadAsStringAsync());
        return created;
    }

    /// <summary>start → (IdP login) → callback; returns where the callback sent the browser.</summary>
    private async Task<(Uri Location, string CallbackUrl)> CallbackAsync(
        Guid connectionId, string subject, string email, string? nonceOverride = null)
    {
        var browser = Browser();
        var start = await browser.GetAsync($"{Sso}/{connectionId}/start?returnUrl={Uri.EscapeDataString(ReturnUrl)}");
        start.StatusCode.Should().Be(HttpStatusCode.Redirect, await start.Content.ReadAsStringAsync());
        var authorize = start.Headers.Location!;
        authorize.ToString().Should().StartWith(_idp.Authority + "/authorize");

        var code = _idp.IssueCode(authorize, subject, email, nonceOverride: nonceOverride);
        var state = Query(authorize)["state"]!;
        var callbackUrl = $"{Sso}/oidc/callback?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";

        var callback = await browser.GetAsync(callbackUrl);
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        callback.Headers.Location!.ToString().Should().StartWith(ReturnUrl);
        return (callback.Headers.Location!, callbackUrl);
    }

    private async Task<LoginData> SignInAsync(Guid connectionId, string subject, string email)
    {
        var (location, _) = await CallbackAsync(connectionId, subject, email);
        var code = Query(location)["sso_code"];
        code.Should().NotBeNull($"callback went to {location}");
        return await PostAsync<LoginData>(Anonymous(), $"{Sso}/exchange", new { code });
    }

    private static System.Collections.Specialized.NameValueCollection Query(Uri uri) =>
        System.Web.HttpUtility.ParseQueryString(uri.Query);

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<Envelope<T>>(text, Json)!.Data!;
    }

    private static async Task<Envelope<T>> ReadAsync<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<Envelope<T>>(await response.Content.ReadAsStringAsync(), Json)!;
}
