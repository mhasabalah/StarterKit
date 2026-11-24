using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using Api.E2ETests.MultiTenancy;
using FluentAssertions;
using Tenancy.Contracts.Tenants;
using Xunit;

namespace Api.E2ETests.Search;

/// <summary>
/// The search admin routes with <c>MultiTenancy:Enabled=true</c>: <c>search.manage</c> comes from the membership only
/// (the owner passes; a non-member and a platform Admin do not), and the platform routes are host-only, so a SuperAdmin
/// reaches them without naming a tenant.
/// </summary>
[Collection(MultiTenantE2ECollection.Name)]
public sealed class SearchTenantAuthorizationTests(MultiTenantE2EFixture fixture)
{
    private const string Api = "/api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private async Task<(Guid TenantId, Guid OwnerId)> CreateTenantAsync()
    {
        var ownerId = Guid.NewGuid();
        var identifier = $"se-{Guid.NewGuid():N}"[..20];
        using var response = await fixture.Client(ownerId).PostAsJsonAsync($"{Api}/tenants", new { name = "search", identifier });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<Envelope<CreateTenantResult>>(Json);
        return (body!.Data!.TenantId, ownerId);
    }

    public static TheoryData<string> TenantRoutes => new()
    {
        $"{Api}/search/indexes",
        $"{Api}/search/analytics/zero-results",
        $"{Api}/search/analytics/daily"
    };

    [Theory]
    [MemberData(nameof(TenantRoutes))]
    public async Task The_owner_passes_and_a_non_member_or_a_platform_admin_is_refused(string url)
    {
        var (tenant, ownerId) = await CreateTenantAsync();

        var owner = await fixture.Client(ownerId, tenant).GetAsync(url);
        owner.StatusCode.Should().Be(HttpStatusCode.OK, await owner.Content.ReadAsStringAsync());

        (await fixture.Client(Guid.NewGuid(), tenant).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = fixture.Factory.CreateClientAs(Guid.NewGuid(), "Admin");
        admin.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");
        admin.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenant.ToString());
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_platform_routes_are_host_only_for_superadmin_and_closed_to_tenant_owners()
    {
        var superAdmin = fixture.Factory.CreateClientAs(Guid.NewGuid(), "SuperAdmin");
        superAdmin.DefaultRequestHeaders.Add(TestAuthHandler.MfaHeader, "true");

        var engine = await superAdmin.GetAsync($"{Api}/admin/search/engine");
        engine.StatusCode.Should().Be(HttpStatusCode.OK, await engine.Content.ReadAsStringAsync());

        var (tenant, ownerId) = await CreateTenantAsync();
        (await fixture.Client(ownerId, tenant).GetAsync($"{Api}/admin/search/engine")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "owning a tenant grants no platform permission");
    }
}
