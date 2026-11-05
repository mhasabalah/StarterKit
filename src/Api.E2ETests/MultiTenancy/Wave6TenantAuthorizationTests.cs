using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Tenants;
using Xunit;

namespace Api.E2ETests.MultiTenancy;

/// <summary>
/// Membership-based authorization of the wave 6 tenant routes (webhooks, product history) with
/// <c>MultiTenancy:Enabled=true</c>: platform roles never grant tenant permissions; only the owner / a role holding the
/// permission does.
/// </summary>
[Collection(MultiTenantE2ECollection.Name)]
public sealed class Wave6TenantAuthorizationTests(MultiTenantE2EFixture fixture)
{
    private const string Api = "/api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record BranchNode(Guid Id, bool IsHeadOffice);

    private async Task<(Guid TenantId, Guid OwnerId)> CreateTenantAsync()
    {
        var ownerId = Guid.NewGuid();
        var identifier = $"w6-{Guid.NewGuid():N}"[..20];
        using var response = await fixture.Client(ownerId).PostAsJsonAsync($"{Api}/tenants", new { name = "w6", identifier });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<Envelope<CreateTenantResult>>(Json);
        return (body!.Data!.TenantId, ownerId);
    }

    public static IEnumerable<object[]> TenantRoutes()
    {
        yield return [$"{Api}/webhooks/endpoints"];
        yield return [$"{Api}/webhooks/deliveries"];
        yield return [$"{Api}/webhooks/event-types"];
    }

    [Theory]
    [MemberData(nameof(TenantRoutes))]
    public async Task Anonymous_is_401(string url)
    {
        // Tenant resolution runs before authorization, so name a tenant: otherwise it answers 400 tenant_required first.
        var (tenant, _) = await CreateTenantAsync();
        using var client = fixture.Factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Tenant", tenant.ToString());

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"{Api}/products/{Guid.NewGuid()}/history")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(TenantRoutes))]
    public async Task Owner_passes_and_a_non_member_or_a_platform_admin_is_refused(string url)
    {
        var (tenant, ownerId) = await CreateTenantAsync();

        (await fixture.Client(ownerId, tenant).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Not a member of the tenant: 403 (tenant_mismatch / no membership).
        (await fixture.Client(Guid.NewGuid(), tenant).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A platform Admin role grants nothing inside a tenant in multi-tenant mode.
        var admin = fixture.Factory.CreateClientAs(Guid.NewGuid(), "Admin");
        admin.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenant.ToString());
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Member_without_the_permission_is_403_on_webhooks_and_product_history_while_the_owner_passes()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var owner = fixture.Client(ownerId, tenant);

        var email = $"m{Guid.NewGuid():N}@example.com";
        using var tree = await owner.GetAsync($"{Api}/branches");
        var headOffice = (await tree.Content.ReadFromJsonAsync<Envelope<List<BranchNode>>>(Json))!.Data!.Single(b => b.IsHeadOffice).Id;
        using var invite = await owner.PostAsJsonAsync($"{Api}/tenant-invitations", new
        {
            email,
            roleIds = Array.Empty<Guid>(),
            branches = new[] { new { branchId = headOffice, includeDescendants = true } }
        });
        invite.StatusCode.Should().Be(HttpStatusCode.OK, await invite.Content.ReadAsStringAsync());
        var token = (await invite.Content.ReadFromJsonAsync<Envelope<InviteMemberResult>>(Json))!.Data!.Token;
        var memberId = Guid.NewGuid();
        using var accept = await fixture.Client(memberId, email: email)
            .PostAsJsonAsync($"{Api}/tenant-invitations/accept", new { token });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());

        var member = fixture.Client(memberId, tenant);
        (await member.GetAsync($"{Api}/webhooks/endpoints")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync($"{Api}/webhooks/endpoints", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"{Api}/products/{Guid.NewGuid()}/history")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Owner: through the gate (the product does not exist: 404, never 401/403).
        (await owner.GetAsync($"{Api}/products/{Guid.NewGuid()}/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
