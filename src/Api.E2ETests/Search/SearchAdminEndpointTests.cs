using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Search;

/// <summary>
/// The search admin API over real HTTP in single-tenant mode: the tenant routes need <c>search.manage</c> (platform
/// Admins hold it for the Default tenant), the platform routes need <c>search.operate</c> (SuperAdmin only), actions are
/// refused to impersonated sessions, and bad input is a 400/404 with the envelope.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class SearchAdminEndpointTests(E2ETestFixture fixture)
{
    private const string Api = "/api/v1";
    private const string UnknownIndex = "test.unknown";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);
    private sealed record EngineData(string Engine, List<JsonElement> Indexes);

    private HttpClient As(string role)
    {
        var client = fixture.CreateClientAs(Guid.NewGuid(), role);
        client.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");
        return client;
    }

    public static TheoryData<string, string> TenantRoutes => new()
    {
        { "GET", $"{Api}/search/indexes" },
        { "GET", $"{Api}/search/indexes/{UnknownIndex}" },
        { "POST", $"{Api}/search/indexes/{UnknownIndex}/rebuild" },
        { "POST", $"{Api}/search/indexes/{UnknownIndex}/drift-check" },
        { "GET", $"{Api}/search/analytics/zero-results" },
        { "GET", $"{Api}/search/analytics/daily" }
    };

    public static TheoryData<string, string> PlatformRoutes => new()
    {
        { "GET", $"{Api}/admin/search/engine" },
        { "POST", $"{Api}/admin/search/indexes/{UnknownIndex}/rebuild-all" }
    };

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url) =>
        method == "GET" ? client.GetAsync(url) : client.PostAsync(url, null);

    [Theory]
    [MemberData(nameof(TenantRoutes))]
    [MemberData(nameof(PlatformRoutes))]
    public async Task Anonymous_is_401(string method, string url) =>
        (await SendAsync(fixture.CreateAnonymousClient(), method, url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Theory]
    [MemberData(nameof(TenantRoutes))]
    public async Task A_user_without_search_manage_is_403_on_the_tenant_routes(string method, string url) =>
        (await SendAsync(As(RoleConstants.User), method, url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData(RoleConstants.Admin)]
    [InlineData(RoleConstants.Support)]
    [InlineData(RoleConstants.Finance)]
    [InlineData(RoleConstants.User)]
    public async Task Only_superadmin_holds_search_operate(string role)
    {
        foreach (var (method, url) in new[] { ("GET", $"{Api}/admin/search/engine"), ("POST", $"{Api}/admin/search/indexes/{UnknownIndex}/rebuild-all") })
            (await SendAsync(As(role), method, url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} {method} {url}");
    }

    [Fact]
    public async Task An_admin_reads_the_indexes_and_the_analytics_of_the_tenant()
    {
        var admin = As(RoleConstants.Admin);

        var indexes = await admin.GetAsync($"{Api}/search/indexes");
        indexes.StatusCode.Should().Be(HttpStatusCode.OK, await indexes.Content.ReadAsStringAsync());

        var daily = await admin.GetFromJsonAsync<Envelope<List<JsonElement>>>($"{Api}/search/analytics/daily", Json);
        daily!.IsSuccess.Should().BeTrue();
        daily.Data.Should().NotBeNull();

        var terms = await admin.GetAsync($"{Api}/search/analytics/zero-results?pageNumber=1&pageSize=5");
        terms.StatusCode.Should().Be(HttpStatusCode.OK, await terms.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_indexes_are_404_and_a_bad_analytics_range_is_400()
    {
        var admin = As(RoleConstants.Admin);

        (await admin.GetAsync($"{Api}/search/indexes/{UnknownIndex}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"{Api}/search/indexes/{UnknownIndex}/rebuild", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"{Api}/search/indexes/{UnknownIndex}/drift-check", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var range = await admin.GetAsync($"{Api}/search/analytics/daily?from=2026-02-01&to=2026-01-01");
        range.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"{Api}/search/analytics/zero-results?indexKey={UnknownIndex}")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_impersonated_session_cannot_rebuild_or_check_an_index()
    {
        var impersonated = As(RoleConstants.Admin);
        impersonated.DefaultRequestHeaders.Add(TestAuthHandler.ActorHeader, Guid.NewGuid().ToString());

        var rebuild = await impersonated.PostAsync($"{Api}/search/indexes/{UnknownIndex}/rebuild", null);
        var drift = await impersonated.PostAsync($"{Api}/search/indexes/{UnknownIndex}/drift-check", null);

        rebuild.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        drift.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await rebuild.Content.ReadAsStringAsync()).Should().Contain("impersonation");
        (await impersonated.GetAsync($"{Api}/search/indexes")).StatusCode.Should().Be(HttpStatusCode.OK, "reading stays allowed");
    }

    [Fact]
    public async Task A_superadmin_sees_the_engine_and_an_unknown_index_cannot_be_rebuilt_everywhere()
    {
        var superAdmin = As(RoleConstants.SuperAdmin);

        var engine = await superAdmin.GetFromJsonAsync<Envelope<EngineData>>($"{Api}/admin/search/engine", Json);
        engine!.Data!.Engine.Should().Be("Postgres");
        engine.Data.Indexes.Should().NotBeNull();

        (await superAdmin.PostAsync($"{Api}/admin/search/indexes/{UnknownIndex}/rebuild-all", null)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }
}
