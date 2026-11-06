using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.E2ETests.Identity;
using FluentAssertions;
using Identity.Contracts.Sso;
using Identity.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tenancy.Contracts.Tenants;
using Xunit;

namespace Api.E2ETests.MultiTenancy;

/// <summary>
/// Enterprise SSO and SCIM with <c>MultiTenancy:Enabled=true</c>: every tenant has its own SAML connection and SCIM
/// directory; one tenant's IdP assertions, admins and directory tokens never reach another tenant. Plus the SAML
/// metadata refresh against a local metadata server (certificate rollover, entity-id pinning).
/// </summary>
[Collection(MultiTenantE2ECollection.Name)]
public sealed class SsoTenantTests(MultiTenantE2EFixture fixture)
{
    private const string Admin = "/api/v1/tenant/sso";
    private const string ReturnUrl = "http://localhost:3000/auth/sso";
    private const string Scim = "/scim/v2";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record MemberRow(Guid UserId, string? Email);

    private sealed record MembersPage(List<MemberRow>? Result);

    /// <summary>A tenant with its owner, its own SAML IdP and an active SAML connection on its own domain.</summary>
    private sealed class TenantSetup(Guid tenantId, HttpClient owner, SamlTestIdp idp, SsoConnectionDto connection, string domain)
        : IDisposable
    {
        public Guid TenantId { get; } = tenantId;

        public HttpClient Owner { get; } = owner;

        public SamlTestIdp Idp { get; } = idp;

        public SsoConnectionDto Connection { get; } = connection;

        public string Domain { get; } = domain;

        public void Dispose() => Idp.Dispose();
    }

    [Fact]
    public async Task Each_tenant_signs_in_through_its_own_connection_and_never_through_another()
    {
        using var a = await SetupTenantAsync("acme");
        using var b = await SetupTenantAsync("globex");
        var browser = Browser();

        // Discovery is per tenant domain.
        var discoverA = await DiscoverAsync(browser, $"ada@{a.Domain}");
        discoverA.ConnectionId.Should().Be(a.Connection.Id);
        (await DiscoverAsync(browser, $"bob@{b.Domain}")).ConnectionId.Should().Be(b.Connection.Id);
        (await DiscoverAsync(browser, "nobody@unconfigured.example")).SsoAvailable.Should().BeFalse();

        // A's IdP signs a response; it works for A and JIT-provisions the member in A only.
        var (requestId, relayState) = await a.Idp.StartAsync(browser, a.Connection.Id, ReturnUrl);
        var emailA = $"ada@{a.Domain}";
        var acs = await SamlTestIdp.PostResponseAsync(browser, a.Connection.Id,
            a.Idp.SignedResponse(a.Connection, requestId, "tenant-a-user", emailA), relayState);
        acs.Headers.Location!.ToString().Should().Contain("sso_code=");

        (await MemberEmailsAsync(a.Owner)).Should().Contain(emailA);
        (await MemberEmailsAsync(b.Owner)).Should().NotContain(emailA, "JIT provisioning is scoped to the connection's tenant");

        // A's signed response replayed against B's ACS (even with B's relay state) is refused.
        var browserB = Browser();
        var (requestIdB, relayStateB) = await b.Idp.StartAsync(browserB, b.Connection.Id, ReturnUrl);
        var crossed = await SamlTestIdp.PostResponseAsync(browserB, b.Connection.Id,
            a.Idp.SignedResponse(a.Connection, requestIdB, "tenant-a-user", emailA), relayStateB);
        crossed.Headers.Location!.ToString().Should().Contain("sso_error=").And.NotContain("sso_code");
        (await MemberEmailsAsync(b.Owner)).Should().NotContain(emailA);
    }

    [Fact]
    public async Task A_tenant_admin_cannot_read_or_change_another_tenants_connection()
    {
        using var a = await SetupTenantAsync("acme");
        using var b = await SetupTenantAsync("globex");
        var url = $"{Admin}/connections/{b.Connection.Id}";

        (await a.Owner.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await a.Owner.PostAsync($"{url}/disable", JsonContent.Create(new { }))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await a.Owner.PostAsync($"{url}/refresh-metadata", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await a.Owner.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await a.Owner.PostAsJsonAsync($"{url}/domains", new { domain = "steal.example" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listed = await a.Owner.GetAsync($"{Admin}/connections");
        var text = await listed.Content.ReadAsStringAsync();
        text.Should().Contain(a.Connection.Id.ToString()).And.NotContain(b.Connection.Id.ToString());

        // B's connection is untouched.
        var read = await b.Owner.GetAsync(url);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync<SsoConnectionDto>(read)).Status.Should().Be(SsoConnectionStatus.Active);
    }

    [Fact]
    public async Task A_scim_token_of_one_tenant_cannot_touch_another_tenants_directory()
    {
        using var a = await SetupTenantAsync("acme");
        using var b = await SetupTenantAsync("globex");
        var tokenA = await CreateDirectoryTokenAsync(a.Owner);
        var tokenB = await CreateDirectoryTokenAsync(b.Owner);

        var created = await ScimClient(tokenB).SendAsync(ScimRequest(HttpMethod.Post, $"{Scim}/Users", new JsonObject
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:User"),
            ["userName"] = $"carol@{b.Domain}",
            ["active"] = true,
            ["emails"] = new JsonArray(new JsonObject { ["value"] = $"carol@{b.Domain}", ["primary"] = true })
        }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = JsonNode.Parse(await created.Content.ReadAsStringAsync())!["id"]!.GetValue<string>();

        var asA = ScimClient(tokenA);
        (await asA.GetAsync($"{Scim}/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = JsonNode.Parse(await (await asA.GetAsync($"{Scim}/Users")).Content.ReadAsStringAsync())!;
        list["Resources"]!.AsArray().Select(r => r!["id"]!.GetValue<string>()).Should().NotContain(id);
        (await asA.SendAsync(ScimRequest(HttpMethod.Patch, $"{Scim}/Users/{id}", new JsonObject
        {
            ["Operations"] = new JsonArray(new JsonObject { ["op"] = "replace", ["value"] = new JsonObject { ["active"] = false } })
        }))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asA.DeleteAsync($"{Scim}/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A cannot provision an email on B's domain either: A never verified it.
        var foreign = await asA.SendAsync(ScimRequest(HttpMethod.Post, $"{Scim}/Users", new JsonObject
        {
            ["userName"] = $"mallory@{b.Domain}",
            ["emails"] = new JsonArray(new JsonObject { ["value"] = $"mallory@{b.Domain}", ["primary"] = true })
        }));
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // B's user is still active.
        var still = JsonNode.Parse(await (await ScimClient(tokenB).GetAsync($"{Scim}/Users/{id}")).Content.ReadAsStringAsync())!;
        still["active"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Metadata_refresh_rolls_the_certificate_over_and_pins_the_entity_id()
    {
        await using var metadata = await MetadataServer.StartAsync();
        var (tenantId, ownerId) = await CreateTenantAsync("meta");
        var owner = fixture.Client(ownerId, tenantId);
        using var idp = new SamlTestIdp("https://idp.meta.example/saml", "https://idp.meta.example/sso");
        metadata.Xml = idp.MetadataXml();

        var connection = await CreateConnectionAsync(owner, idp, $"meta-{Guid.NewGuid():N}.example", metadata.Url);
        connection.SamlIdpMetadataUrl.Should().Be(metadata.Url);

        // Same metadata: nothing changes, only RefreshedAt moves.
        var unchanged = await RefreshAsync(owner, connection.Id);
        unchanged.Outcome.Should().Be("Unchanged");
        var afterUnchanged = await GetConnectionAsync(owner, connection.Id);
        afterUnchanged.SamlMetadataRefreshedAt.Should().NotBeNull();
        afterUnchanged.Version.Should().Be(connection.Version, "an unchanged refresh must not bump the version");

        // Certificate rollover at the IdP: the new key is picked up.
        using var rolled = SamlTestIdp.SelfSigned("CN=rolled-idp");
        metadata.Xml = idp.MetadataXml(null, rolled);
        var updated = await RefreshAsync(owner, connection.Id);
        updated.Outcome.Should().Be("Updated");
        var afterUpdate = await GetConnectionAsync(owner, connection.Id);
        afterUpdate.SamlIdpCertificates.Should().ContainSingle().Which.Thumbprint.Should().Be(rolled.Thumbprint);
        afterUpdate.SamlMetadataLastError.Should().BeNull();

        // Metadata of ANOTHER entity is rejected and recorded; the keys stay.
        metadata.Xml = idp.MetadataXml("https://evil.example/saml");
        var rejected = await RefreshAsync(owner, connection.Id);
        rejected.Outcome.Should().Be("Failed");
        rejected.Error.Should().Be("metadata_entity_id_mismatch");
        var afterReject = await GetConnectionAsync(owner, connection.Id);
        afterReject.SamlMetadataLastError.Should().Be("metadata_entity_id_mismatch");
        afterReject.SamlIdpCertificates.Should().ContainSingle().Which.Thumbprint.Should().Be(rolled.Thumbprint);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private async Task<TenantSetup> SetupTenantAsync(string prefix)
    {
        var (tenantId, ownerId) = await CreateTenantAsync(prefix);
        var owner = fixture.Client(ownerId, tenantId);
        var token = Guid.NewGuid().ToString("N")[..12];
        var idp = new SamlTestIdp($"https://idp.{prefix}-{token}.example/saml", $"https://idp.{prefix}-{token}.example/sso");
        var domain = $"{prefix}-{token}.example";
        var connection = await CreateConnectionAsync(owner, idp, domain, metadataUrl: null);
        return new TenantSetup(tenantId, owner, idp, connection, domain);
    }

    private async Task<(Guid TenantId, Guid OwnerId)> CreateTenantAsync(string prefix)
    {
        var ownerId = Guid.NewGuid();
        var identifier = $"{prefix}-{Guid.NewGuid():N}"[..20];
        using var response = await fixture.Client(ownerId).PostAsJsonAsync("/api/v1/tenants", new { name = prefix, identifier });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Envelope<CreateTenantResult>>(Json);
        return (body!.Data!.TenantId, ownerId);
    }

    private async Task<SsoConnectionDto> CreateConnectionAsync(HttpClient owner, SamlTestIdp idp, string domain, string? metadataUrl)
    {
        var created = await owner.PostAsJsonAsync($"{Admin}/connections", new
        {
            name = "Corporate SAML",
            protocol = "Saml",
            samlIdpEntityId = idp.EntityId,
            samlIdpSsoUrl = idp.SsoUrl,
            samlIdpMetadataUrl = metadataUrl,
            samlIdpCertificates = new[] { idp.CertificateBase64 },
            jitProvisioning = true
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var connection = await ReadAsync<SsoConnectionDto>(created);

        (await owner.PostAsJsonAsync($"{Admin}/connections/{connection.Id}/domains", new { domain }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationIdentityDbContext>();
            var entity = await db.SsoConnections.Include(c => c.Domains).FirstAsync(c => c.Id == connection.Id);
            entity.MarkDomainVerified(domain, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var activated = await owner.PostAsJsonAsync($"{Admin}/connections/{connection.Id}/activate", new { });
        activated.StatusCode.Should().Be(HttpStatusCode.OK, await activated.Content.ReadAsStringAsync());
        return await ReadAsync<SsoConnectionDto>(activated);
    }

    private async Task<RefreshSamlMetadataResult> RefreshAsync(HttpClient owner, Guid connectionId)
    {
        var response = await owner.PostAsync($"{Admin}/connections/{connectionId}/refresh-metadata", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await ReadAsync<RefreshSamlMetadataResult>(response);
    }

    private static async Task<SsoConnectionDto> GetConnectionAsync(HttpClient owner, Guid id) =>
        await ReadAsync<SsoConnectionDto>(await owner.GetAsync($"{Admin}/connections/{id}"));

    private HttpClient Browser() =>
        fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<SsoDiscoveryResult> DiscoverAsync(HttpClient client, string email) =>
        await ReadAsync<SsoDiscoveryResult>(await client.PostAsJsonAsync("/api/auth/sso/discover", new { email }));

    private static async Task<List<string>> MemberEmailsAsync(HttpClient owner)
    {
        var response = await owner.GetAsync("/api/v1/members?pageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var page = await ReadAsync<MembersPage>(response);
        return (page.Result ?? []).Select(m => m.Email ?? string.Empty).ToList();
    }

    private static async Task<string> CreateDirectoryTokenAsync(HttpClient owner)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/tenant/scim/directories", new { name = "Okta" });
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return JsonNode.Parse(text)!["data"]!["token"]!.GetValue<string>();
    }

    private HttpClient ScimClient(string token)
    {
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpRequestMessage ScimRequest(HttpMethod method, string url, JsonObject body) => new(method, url)
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/scim+json")
    };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<Envelope<T>>(text, Json)!.Data!;
    }

    /// <summary>A loopback HTTP server that serves the IdP metadata document set in <see cref="Xml"/>.</summary>
    private sealed class MetadataServer : IAsyncDisposable
    {
        private WebApplication? _app;

        public string Xml { get; set; } = "<empty/>";

        public string Url { get; private set; } = default!;

        public static async Task<MetadataServer> StartAsync()
        {
            var server = new MetadataServer();
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.MapGet("/metadata", () => Results.Text(server.Xml, "application/samlmetadata+xml"));
            await app.StartAsync();
            server._app = app;
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            server.Url = address.TrimEnd('/') + "/metadata";
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            if (_app is not null)
                await _app.DisposeAsync();
        }
    }
}
