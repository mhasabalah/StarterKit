using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Contracts.Scim;
using Identity.Contracts.Sso;
using Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// SCIM 2.0 over real HTTP with the request shapes Okta and Entra ID send: idempotent create, filters,
/// deprovisioning through PATCH and DELETE, group membership patches, token rotation, domain proof and
/// directory isolation. Responses are checked for SCIM's own content type and error schema.
/// </summary>
[Collection(E2ECollection.Name)]
public class ScimE2ETests(E2ETestFixture fixture) : IAsyncLifetime
{
    private const string Scim = "/scim/v2";
    private const string ScimJson = "application/scim+json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string _domain = default!;
    private HttpClient _scim = default!;
    private string _token = default!;
    private readonly List<Guid> _directories = [];

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    public async Task InitializeAsync()
    {
        _domain = $"globex-{Guid.NewGuid():N}.example";
        await VerifiedDomainAsync(_domain);
        (_, _token) = await CreateDirectoryAsync("Okta");
        _scim = ScimClient(_token);
    }

    /// <summary>Every test shares the Default tenant (max 5 directories): remove what this test created.</summary>
    public async Task DisposeAsync()
    {
        foreach (var id in _directories)
            await AdminClient().DeleteAsync($"/api/v1/tenant/scim/directories/{id}");
    }

    [Fact]
    public async Task Okta_style_create_is_idempotent_and_filterable()
    {
        var userName = $"ada@{_domain}";

        var lookup = await GetJsonAsync($"{Scim}/Users?filter={Uri.EscapeDataString($"userName eq \"{userName}\"")}");
        lookup["totalResults"]!.GetValue<int>().Should().Be(0);

        var created = await SendAsync(HttpMethod.Post, $"{Scim}/Users", User(userName, "okta-001"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Content.Headers.ContentType!.MediaType.Should().Be(ScimJson);
        created.Headers.Location.Should().NotBeNull();
        var body = await BodyAsync(created);
        body["active"]!.GetValue<bool>().Should().BeTrue();
        body["name"]!["givenName"]!.GetValue<string>().Should().Be("Ada");
        var id = body["id"]!.GetValue<string>();

        var again = await SendAsync(HttpMethod.Post, $"{Scim}/Users", User(userName, "okta-001"));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(again))["scimType"]!.GetValue<string>().Should().Be("uniqueness");

        var byExternal = await GetJsonAsync($"{Scim}/Users?filter={Uri.EscapeDataString("externalId eq \"okta-001\"")}");
        byExternal["Resources"]!.AsArray().Single()!["id"]!.GetValue<string>().Should().Be(id);

        var byEmail = await GetJsonAsync($"{Scim}/Users?filter={Uri.EscapeDataString($"emails[type eq \"work\"].value eq \"{userName}\"")}");
        byEmail["totalResults"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task Okta_deactivation_patch_suspends_and_reactivation_restores()
    {
        var id = await CreateUserAsync($"grace@{_domain}");

        var deactivated = await SendAsync(HttpMethod.Patch, $"{Scim}/Users/{id}", new JsonObject
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:api:messages:2.0:PatchOp"),
            ["Operations"] = new JsonArray(new JsonObject { ["op"] = "replace", ["value"] = new JsonObject { ["active"] = false } })
        });
        deactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyAsync(deactivated))["active"]!.GetValue<bool>().Should().BeFalse();

        var reactivated = await SendAsync(HttpMethod.Patch, $"{Scim}/Users/{id}", new JsonObject
        {
            ["Operations"] = new JsonArray(new JsonObject { ["op"] = "replace", ["value"] = new JsonObject { ["active"] = true } })
        });
        (await BodyAsync(reactivated))["active"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Entra_style_patch_with_string_booleans_and_paths_is_understood()
    {
        var id = await CreateUserAsync($"linus@{_domain}");

        var response = await SendAsync(HttpMethod.Patch, $"{Scim}/Users/{id}", new JsonObject
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:api:messages:2.0:PatchOp"),
            ["Operations"] = new JsonArray(
                new JsonObject { ["op"] = "Replace", ["path"] = "active", ["value"] = "False" },
                new JsonObject { ["op"] = "Replace", ["path"] = "name.familyName", ["value"] = "Torvalds" })
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body["active"]!.GetValue<bool>().Should().BeFalse();
        body["name"]!["familyName"]!.GetValue<string>().Should().Be("Torvalds");
    }

    [Fact]
    public async Task Delete_deprovisions_and_the_resource_is_gone()
    {
        var id = await CreateUserAsync($"margaret@{_domain}");

        (await SendAsync(HttpMethod.Delete, $"{Scim}/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await SendAsync(HttpMethod.Get, $"{Scim}/Users/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyAsync(get))["schemas"]![0]!.GetValue<string>().Should().Contain("Error");
    }

    [Fact]
    public async Task Emails_on_unverified_domains_are_refused()
    {
        var response = await SendAsync(HttpMethod.Post, $"{Scim}/Users", User("victim@not-ours.example", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyAsync(response))["scimType"]!.GetValue<string>().Should().Be("invalidValue");
    }

    [Fact]
    public async Task Group_membership_patches_are_incremental()
    {
        var a = await CreateUserAsync($"a@{_domain}");
        var b = await CreateUserAsync($"b@{_domain}");

        var created = await SendAsync(HttpMethod.Post, $"{Scim}/Groups", new JsonObject
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:Group"),
            ["displayName"] = "Engineering",
            ["members"] = new JsonArray(new JsonObject { ["value"] = a })
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var groupId = (await BodyAsync(created))["id"]!.GetValue<string>();

        await SendAsync(HttpMethod.Patch, $"{Scim}/Groups/{groupId}", new JsonObject
        {
            ["Operations"] = new JsonArray(new JsonObject
            {
                ["op"] = "add", ["path"] = "members", ["value"] = new JsonArray(new JsonObject { ["value"] = b })
            })
        });
        var removeA = await SendAsync(HttpMethod.Patch, $"{Scim}/Groups/{groupId}", new JsonObject
        {
            ["Operations"] = new JsonArray(new JsonObject { ["op"] = "remove", ["path"] = $"members[value eq \"{a}\"]" })
        });

        var members = (await BodyAsync(removeA))["members"]!.AsArray().Select(m => m!["value"]!.GetValue<string>());
        members.Should().BeEquivalentTo([b]);

        var withoutMembers = await GetJsonAsync($"{Scim}/Groups/{groupId}?excludedAttributes=members");
        withoutMembers["members"].Should().BeNull();
    }

    [Fact]
    public async Task Invalid_or_rotated_tokens_get_a_scim_401()
    {
        var bad = await ScimClient("scim_000000000000_nope").GetAsync($"{Scim}/Users");
        bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (directoryId, token) = await CreateDirectoryAsync("Rotating");
        (await ScimClient(token).GetAsync($"{Scim}/Users")).StatusCode.Should().Be(HttpStatusCode.OK);

        var rotated = await AdminClient().PostAsync($"/api/v1/tenant/scim/directories/{directoryId}/rotate-token", null);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ScimClient(token).GetAsync($"{Scim}/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_directory_never_sees_another_directorys_users()
    {
        var id = await CreateUserAsync($"isolated@{_domain}");
        var (_, otherToken) = await CreateDirectoryAsync("Other IdP");

        (await ScimClient(otherToken).GetAsync($"{Scim}/Users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = JsonNode.Parse(await (await ScimClient(otherToken).GetAsync($"{Scim}/Users")).Content.ReadAsStringAsync())!;
        list["Resources"]!.AsArray().Select(r => r!["id"]!.GetValue<string>()).Should().NotContain(id);
    }

    [Fact]
    public async Task Unsupported_filters_are_rejected_not_ignored()
    {
        var response = await SendAsync(HttpMethod.Get, $"{Scim}/Users?filter={Uri.EscapeDataString("title co \"x\"")}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyAsync(response))["scimType"]!.GetValue<string>().Should().Be("invalidFilter");
    }

    [Fact]
    public async Task Discovery_documents_describe_the_implementation()
    {
        var config = await GetJsonAsync($"{Scim}/ServiceProviderConfig");
        config["patch"]!["supported"]!.GetValue<bool>().Should().BeTrue();
        config["bulk"]!["supported"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Groups_map_only_to_roles_of_the_tenant()
    {
        var member = await CreateUserAsync($"mapped@{_domain}");
        var created = await SendAsync(HttpMethod.Post, $"{Scim}/Groups", new JsonObject
        {
            ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:Group"),
            ["displayName"] = "Admins",
            ["members"] = new JsonArray(new JsonObject { ["value"] = member })
        });
        var groupId = (await BodyAsync(created))["id"]!.GetValue<string>();
        var directoryId = _directories[0];
        var url = $"/api/v1/tenant/scim/directories/{directoryId}/groups/{groupId}/role";

        var foreign = await AdminClient().PutAsJsonAsync(url, new { tenantRoleId = Guid.NewGuid() });
        foreign.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyAsync(foreign))["errorCode"]!.GetValue<string>().Should().Be("tenant_role_not_found");

        var roles = await BodyAsync(await AdminClient().GetAsync("/api/v1/tenant-roles"));
        var roleId = roles["data"]!.AsArray()[0]!["id"]!.GetValue<string>();
        var mapped = await AdminClient().PutAsJsonAsync(url, new { tenantRoleId = roleId });
        mapped.StatusCode.Should().Be(HttpStatusCode.OK, await mapped.Content.ReadAsStringAsync());
        (await BodyAsync(mapped))["data"]!["tenantRoleId"]!.GetValue<string>().Should().Be(roleId);
    }

    [Fact]
    public async Task The_directory_list_reports_resource_counts()
    {
        await CreateUserAsync($"counted@{_domain}");

        var list = await BodyAsync(await AdminClient().GetAsync("/api/v1/tenant/scim/directories"));

        var mine = list["data"]!.AsArray().Single(d => d!["id"]!.GetValue<Guid>() == _directories[0])!;
        mine["users"]!.GetValue<int>().Should().Be(1);
        mine["groups"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task Directory_admin_routes_answer_401_anonymous_and_403_without_the_tenant_permission()
    {
        const string url = "/api/v1/tenant/scim/directories";

        (await fixture.CreateAnonymousClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User).GetAsync(url))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User).PostAsJsonAsync(url, new { name = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_user_session_never_reaches_the_scim_protocol()
    {
        (await fixture.CreateAnonymousClient().GetAsync($"{Scim}/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AdminClient().GetAsync($"{Scim}/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_disabled_directory_is_refused()
    {
        var (directoryId, token) = await CreateDirectoryAsync("Paused");
        var directories = await BodyAsync(await AdminClient().GetAsync("/api/v1/tenant/scim/directories"));
        var version = directories["data"]!.AsArray().Single(d => d!["id"]!.GetValue<Guid>() == directoryId)!["version"]!.GetValue<long>();

        var disabled = await AdminClient().PutAsJsonAsync($"/api/v1/tenant/scim/directories/{directoryId}/enabled",
            new { enabled = false, version });
        disabled.StatusCode.Should().Be(HttpStatusCode.OK, await disabled.Content.ReadAsStringAsync());

        (await ScimClient(token).GetAsync($"{Scim}/Users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private const string Enterprise = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User";

    [Fact]
    public async Task Enterprise_extension_is_stored_patched_and_returned()
    {
        var managerId = await CreateUserAsync($"boss@{_domain}");
        var resource = User($"worker@{_domain}", "okta-ent-1");
        ((JsonArray)resource["schemas"]!).Add(Enterprise);
        resource[Enterprise] = new JsonObject
        {
            ["employeeNumber"] = "E-42",
            ["department"] = "Platform",
            ["manager"] = new JsonObject { ["value"] = managerId }
        };

        var created = await SendAsync(HttpMethod.Post, $"{Scim}/Users", resource);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await BodyAsync(created);
        body["schemas"]!.AsArray().Select(x => x!.GetValue<string>()).Should().Contain(Enterprise);
        body[Enterprise]!["employeeNumber"]!.GetValue<string>().Should().Be("E-42");
        body[Enterprise]!["manager"]!["value"]!.GetValue<string>().Should().Be(managerId);
        var id = body["id"]!.GetValue<string>();

        // Entra style: full URN paths, plus manager.value; Okta style: path-less object keyed by the extension URN.
        var patched = await SendAsync(HttpMethod.Patch, $"{Scim}/Users/{id}", new JsonObject
        {
            ["Operations"] = new JsonArray(
                new JsonObject { ["op"] = "Replace", ["path"] = $"{Enterprise}:department", ["value"] = "Security" },
                new JsonObject { ["op"] = "Add", ["path"] = $"{Enterprise}:costCenter", ["value"] = "CC-7" },
                new JsonObject { ["op"] = "Remove", ["path"] = $"{Enterprise}:manager" },
                new JsonObject
                {
                    ["op"] = "replace",
                    ["value"] = new JsonObject { [Enterprise] = new JsonObject { ["division"] = "EMEA" } }
                })
        });
        patched.StatusCode.Should().Be(HttpStatusCode.OK, await patched.Content.ReadAsStringAsync());
        var after = (await BodyAsync(patched))[Enterprise]!;
        after["department"]!.GetValue<string>().Should().Be("Security");
        after["costCenter"]!.GetValue<string>().Should().Be("CC-7");
        after["division"]!.GetValue<string>().Should().Be("EMEA");
        after["employeeNumber"]!.GetValue<string>().Should().Be("E-42", "untouched attributes stay");
        after["manager"].Should().BeNull("the manager was removed");

        var fetched = await GetJsonAsync($"{Scim}/Users/{id}");
        fetched[Enterprise]!["department"]!.GetValue<string>().Should().Be("Security");
    }

    [Fact]
    public async Task An_unknown_patch_path_is_a_400_invalidPath_but_known_unstored_attributes_are_accepted()
    {
        var id = await CreateUserAsync($"paths@{_domain}");

        var unknown = await SendAsync(HttpMethod.Patch, $"{Scim}/Users/{id}", new JsonObject
        {
            ["Operations"] = new JsonArray(new JsonObject { ["op"] = "replace", ["path"] = "favouriteColour", ["value"] = "blue" })
        });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BodyAsync(unknown))["scimType"]!.GetValue<string>().Should().Be("invalidPath");

        var unknownEnterprise = await SendAsync(HttpMethod.Patch, $"{Scim}/Users/{id}", new JsonObject
        {
            ["Operations"] = new JsonArray(new JsonObject { ["op"] = "replace", ["path"] = $"{Enterprise}:shoeSize", ["value"] = "9" })
        });
        (await BodyAsync(unknownEnterprise))["scimType"]!.GetValue<string>().Should().Be("invalidPath");

        // What Entra / Okta send when mapped, but this server does not store: accepted, not an error.
        var known = await SendAsync(HttpMethod.Patch, $"{Scim}/Users/{id}", new JsonObject
        {
            ["Operations"] = new JsonArray(
                new JsonObject { ["op"] = "Replace", ["path"] = "phoneNumbers[type eq \"work\"].value", ["value"] = "+20100" },
                new JsonObject { ["op"] = "Replace", ["path"] = "title", ["value"] = "Engineer" },
                new JsonObject { ["op"] = "Replace", ["path"] = "displayName", ["value"] = "Ada L." })
        });
        known.StatusCode.Should().Be(HttpStatusCode.OK, await known.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Discovery_lists_the_enterprise_extension()
    {
        var types = await GetJsonAsync($"{Scim}/ResourceTypes");
        var user = types["Resources"]!.AsArray().Single(r => r!["name"]!.GetValue<string>() == "User")!;
        user["schemaExtensions"]![0]!["schema"]!.GetValue<string>().Should().Be(Enterprise);

        var schemas = await GetJsonAsync($"{Scim}/Schemas");
        schemas["Resources"]!.AsArray().Select(r => r!["id"]!.GetValue<string>()).Should().Contain(Enterprise);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static JsonObject User(string userName, string? externalId) => new()
    {
        ["schemas"] = new JsonArray("urn:ietf:params:scim:schemas:core:2.0:User"),
        ["userName"] = userName,
        ["externalId"] = externalId,
        ["active"] = true,
        ["name"] = new JsonObject { ["givenName"] = "Ada", ["familyName"] = "Lovelace" },
        ["emails"] = new JsonArray(new JsonObject { ["value"] = userName, ["type"] = "work", ["primary"] = true })
    };

    private async Task<string> CreateUserAsync(string userName)
    {
        var response = await SendAsync(HttpMethod.Post, $"{Scim}/Users", User(userName, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await BodyAsync(response))["id"]!.GetValue<string>();
    }

    private HttpClient AdminClient()
    {
        var client = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");
        return client;
    }

    private HttpClient ScimClient(string token)
    {
        var client = fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(Guid Id, string Token)> CreateDirectoryAsync(string name)
    {
        var response = await AdminClient().PostAsJsonAsync("/api/v1/tenant/scim/directories", new { name });
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        var created = JsonSerializer.Deserialize<Envelope<ScimDirectoryCreatedDto>>(text, Json)!.Data!;
        created.Token.Should().StartWith("scim_");
        created.Directory.BaseUrl.Should().EndWith("/scim/v2");
        _directories.Add(created.Directory.Id);
        return (created.Directory.Id, created.Token);
    }

    /// <summary>An SSO connection of the Default tenant with <paramref name="domain"/> verified (DNS simulated).</summary>
    private async Task VerifiedDomainAsync(string domain)
    {
        var create = await AdminClient().PostAsJsonAsync("/api/v1/tenant/sso/connections", new
        {
            name = "Globex",
            protocol = "Oidc",
            oidcAuthority = "https://idp.globex.example",
            oidcClientId = "c",
            oidcClientSecret = "s"
        });
        var connection = JsonSerializer.Deserialize<Envelope<SsoConnectionDto>>(await create.Content.ReadAsStringAsync(), Json)!.Data!;
        (await AdminClient().PostAsJsonAsync($"/api/v1/tenant/sso/connections/{connection.Id}/domains", new { domain }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await fixture.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationIdentityDbContext>();
            var entity = await db.SsoConnections.Include(c => c.Domains).FirstAsync(c => c.Id == connection.Id);
            entity.MarkDomainVerified(domain, DateTime.UtcNow);
            await db.SaveChangesAsync();
        });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, JsonObject? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, ScimJson);
        return await _scim.SendAsync(request);
    }

    private async Task<JsonNode> GetJsonAsync(string url)
    {
        var response = await _scim.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!;
    }

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
}
