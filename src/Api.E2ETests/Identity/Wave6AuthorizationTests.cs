using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Identity;

/// <summary>
/// Authorization of every wave 6 route group over real HTTP: anonymous 401, wrong role / missing permission 403, the
/// right caller gets through (not 401/403), and API keys: rejected on a <c>DisallowApiKey</c> route, accepted on a
/// scoped one, and never able to use role checks to get around their scopes.
/// Tenant-permission routes run here in single-tenant mode (Admin holds the platform copy of the tenant permissions);
/// the membership side is in <c>MultiTenancy/Wave6TenantAuthorizationTests</c>.
/// </summary>
[Collection(E2ECollection.Name)]
public class Wave6AuthorizationTests(E2ETestFixture fixture)
{
    private const string Api = "/api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient As(string? role) => role is null
        ? fixture.CreateAnonymousClient()
        : fixture.CreateClientAs(Guid.NewGuid(), role);

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>Every guarded route: method, url, and a signed-in role that must be refused (403).</summary>
    public static IEnumerable<object[]> Guarded()
    {
        var id = Guid.NewGuid();
        yield return ["GET", $"{Api}/admin/users/{id}/sessions", RoleConstants.User];
        yield return ["DELETE", $"{Api}/admin/users/{id}/sessions/{Guid.NewGuid()}", RoleConstants.User];
        yield return ["GET", $"{Api}/admin/users/{id}/api-keys", RoleConstants.Support];
        yield return ["DELETE", $"{Api}/admin/users/{id}/api-keys/{Guid.NewGuid()}", RoleConstants.Support];
        yield return ["GET", $"{Api}/admin/coupons", RoleConstants.User];
        yield return ["GET", $"{Api}/admin/coupons/{id}", RoleConstants.Support];
        yield return ["POST", $"{Api}/admin/coupons", RoleConstants.Support];
        yield return ["PUT", $"{Api}/admin/coupons/{id}", RoleConstants.User];
        yield return ["DELETE", $"{Api}/admin/coupons/{id}", RoleConstants.User];
        yield return ["GET", $"{Api}/admin/feature-flags", RoleConstants.Admin]; // Admin lacks feature-flags.manage
        yield return ["GET", $"{Api}/admin/feature-flags/some-flag", RoleConstants.Finance];
        yield return ["POST", $"{Api}/admin/feature-flags", RoleConstants.Admin];
        yield return ["PUT", $"{Api}/admin/feature-flags/some-flag", RoleConstants.User];
        yield return ["DELETE", $"{Api}/admin/feature-flags/some-flag", RoleConstants.Admin];
        yield return ["GET", $"{Api}/webhooks/event-types", RoleConstants.User];
        yield return ["GET", $"{Api}/webhooks/endpoints", RoleConstants.User];
        yield return ["GET", $"{Api}/webhooks/endpoints/{id}", RoleConstants.Support];
        yield return ["POST", $"{Api}/webhooks/endpoints", RoleConstants.User];
        yield return ["PUT", $"{Api}/webhooks/endpoints/{id}", RoleConstants.User];
        yield return ["DELETE", $"{Api}/webhooks/endpoints/{id}", RoleConstants.Finance];
        yield return ["POST", $"{Api}/webhooks/endpoints/{id}/rotate-secret", RoleConstants.User];
        yield return ["POST", $"{Api}/webhooks/endpoints/{id}/ping", RoleConstants.User];
        yield return ["GET", $"{Api}/webhooks/deliveries", RoleConstants.User];
        yield return ["GET", $"{Api}/webhooks/deliveries/{id}", RoleConstants.Support];
        yield return ["POST", $"{Api}/webhooks/deliveries/{id}/redeliver", RoleConstants.User];
        yield return ["GET", $"{Api}/products/{id}/history", RoleConstants.User];
    }

    /// <summary>Routes that only need a signed-in user: anonymous is 401.</summary>
    public static IEnumerable<object[]> SignedInOnly()
    {
        yield return ["GET", $"{Api}/me/sessions"];
        yield return ["DELETE", $"{Api}/me/sessions/{Guid.NewGuid()}"];
        yield return ["POST", $"{Api}/me/sessions/revoke-others"];
        yield return ["GET", $"{Api}/me/api-keys"];
        yield return ["POST", $"{Api}/me/api-keys"];
        yield return ["DELETE", $"{Api}/me/api-keys/{Guid.NewGuid()}"];
        yield return ["POST", $"{Api}/subscriptions/me/coupons/redeem"];
        yield return ["POST", $"{Api}/subscriptions/trial"];
        yield return ["GET", $"{Api}/subscriptions/me/usage"];
        yield return ["GET", $"{Api}/me/feature-flags"];
    }

    [Theory]
    [MemberData(nameof(Guarded))]
    public async Task Guarded_route_is_401_anonymous(string method, string url, string refusedRole)
    {
        refusedRole.Should().NotBeNullOrEmpty();
        (await StatusAsync(As(null), new HttpMethod(method), url, new { })).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Guarded))]
    public async Task Guarded_route_is_403_without_the_permission(string method, string url, string refusedRole)
    {
        (await StatusAsync(As(refusedRole), new HttpMethod(method), url, new { })).Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(SignedInOnly))]
    public async Task Signed_in_route_is_401_anonymous(string method, string url)
    {
        (await StatusAsync(As(null), new HttpMethod(method), url, new { })).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Callers_with_the_permission_get_through_the_gate()
    {
        var id = Guid.NewGuid();

        // Support holds sessions.manage; Admin holds api-keys.manage, the tenant permissions and audit; Finance holds
        // billing.coupons.manage; SuperAdmin holds feature-flags.manage.
        (await StatusAsync(As(RoleConstants.Support), HttpMethod.Get, $"{Api}/admin/users/{id}/sessions"))
            .Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);
        (await StatusAsync(As(RoleConstants.Admin), HttpMethod.Get, $"{Api}/admin/users/{id}/api-keys"))
            .Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);
        (await StatusAsync(As(RoleConstants.Finance), HttpMethod.Get, $"{Api}/admin/coupons")).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(As(RoleConstants.SuperAdmin), HttpMethod.Get, $"{Api}/admin/feature-flags")).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(As(RoleConstants.Admin), HttpMethod.Get, $"{Api}/webhooks/endpoints")).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(As(RoleConstants.Admin), HttpMethod.Get, $"{Api}/webhooks/deliveries")).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(As(RoleConstants.Admin), HttpMethod.Get, $"{Api}/webhooks/event-types")).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(As(RoleConstants.Admin), HttpMethod.Get, $"{Api}/products/{id}/history"))
            .Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_in_users_reach_their_own_routes()
    {
        var user = As(RoleConstants.User);

        (await StatusAsync(user, HttpMethod.Get, $"{Api}/me/feature-flags")).Should().Be(HttpStatusCode.OK);
        foreach (var url in new[] { "/subscriptions/me/usage", "/me/sessions", "/me/api-keys" })
        {
            (await StatusAsync(user, HttpMethod.Get, Api + url))
                .Should().NotBe(HttpStatusCode.Forbidden).And.NotBe(HttpStatusCode.Unauthorized, url);
        }
    }

    // ----- API keys -----

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record Created(string ApiKey);

    /// <summary>A real user with the given role, plus a real key for it created through the API.</summary>
    private async Task<string> KeyFor(string role, params string[] scopes)
    {
        var userId = await fixture.WithScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = $"key{Guid.NewGuid():N}", Email = $"key{Guid.NewGuid():N}@example.com",
                EmailConfirmed = true, IsActive = true, IsProfileComplete = true
            };
            (await users.CreateAsync(user, "E2e!Passw0rd")).Succeeded.Should().BeTrue();
            (await users.AddToRolesAsync(user, [RoleConstants.User, role])).Succeeded.Should().BeTrue();
            return user.Id;
        });

        using var response = await fixture.CreateClientAs(userId, RoleConstants.User, role)
            .PostAsJsonAsync($"{Api}/me/api-keys", new { name = "e2e", scopes });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Envelope<Created>>(Json);
        return body!.Data!.ApiKey;
    }

    [Fact]
    public async Task Api_key_is_rejected_on_a_DisallowApiKey_route_and_accepted_on_a_scoped_one()
    {
        var key = await KeyFor(RoleConstants.Support, Permissions.BillingRead);
        var client = fixture.Factory.CreateClientWithApiKey(key);

        using var restricted = await client.GetAsync($"{Api}/me/sessions");
        restricted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await restricted.Content.ReadAsStringAsync()).Should().Contain(ApiKeyClaimTypes.RestrictedErrorCode);
        (await StatusAsync(client, HttpMethod.Post, $"{Api}/me/api-keys", new { name = "x", scopes = new[] { Permissions.BillingRead } }))
            .Should().Be(HttpStatusCode.Forbidden, "a key can never mint keys");

        (await client.GetAsync($"{Api}/admin/subscriptions/pending")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/identity/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the owner may read users, but users.read is not one of the key's scopes");
    }

    [Fact]
    public async Task Invalid_api_key_is_401()
    {
        var client = fixture.Factory.CreateClientWithApiKey("sk_zzzzzzzz_" + new string('A', 43));

        (await client.GetAsync($"{Api}/admin/subscriptions/pending")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_owned_api_key_cannot_use_role_checks_to_escape_its_scopes()
    {
        var key = await KeyFor(RoleConstants.Admin, Permissions.BillingRead);
        var viaKey = fixture.Factory.CreateClientWithApiKey(key);

        // The Admin itself passes the AdminOnly gate (400: empty body); the Admin's key does not (403).
        (await StatusAsync(fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin), HttpMethod.Post, $"{Api}/attribute-types", new { }))
            .Should().NotBe(HttpStatusCode.Forbidden);
        (await StatusAsync(viaKey, HttpMethod.Post, $"{Api}/attribute-types", new { })).Should().Be(HttpStatusCode.Forbidden);

        // Its one scope still works; permissions it holds as Admin but was not scoped for do not.
        (await viaKey.GetAsync($"{Api}/admin/subscriptions/pending")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viaKey.GetAsync($"{Api}/admin/coupons")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viaKey.GetAsync($"{Api}/webhooks/endpoints")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
