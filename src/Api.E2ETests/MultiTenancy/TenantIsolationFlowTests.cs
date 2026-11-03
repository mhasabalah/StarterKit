using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.E2ETests.Infrastructure;
using Catalog.Contracts.Products.Dtos;
using FluentAssertions;
using Tenancy.Contracts.Branches;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Tenants;
using Xunit;

namespace Api.E2ETests.MultiTenancy;

/// <summary>
/// Multi-tenancy over real HTTP (booted API + Postgres, MultiTenancy:Enabled=true): self-service tenants, data
/// isolation between tenants, tenant resolution rules, invitations and branch management.
/// </summary>
[Collection(MultiTenantE2ECollection.Name)]
public sealed class TenantIsolationFlowTests(MultiTenantE2EFixture fixture)
{
    private const string Products = "/api/v1/products";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors);

    private sealed record CreatedResponse(Guid Id);

    private async Task<(Guid TenantId, Guid OwnerId)> CreateTenantAsync(string prefix)
    {
        var ownerId = Guid.NewGuid();
        var identifier = $"{prefix}-{Guid.NewGuid():N}"[..20];
        var result = await ReadAsync<CreateTenantResult>(
            await fixture.Client(ownerId).PostAsJsonAsync("/api/v1/tenants", new { name = prefix, identifier }),
            HttpStatusCode.Created);

        result.Status.Should().Be(Shared.Core.MultiTenancy.TenantStatus.Active);
        return (result.TenantId, ownerId);
    }

    [Fact]
    public async Task Tenants_never_see_each_others_data()
    {
        var (tenantA, ownerA) = await CreateTenantAsync("acme");
        var (tenantB, ownerB) = await CreateTenantAsync("globex");
        var clientA = fixture.Client(ownerA, tenantA);
        var clientB = fixture.Client(ownerB, tenantB);

        // The same SKU is fine in two tenants (uniqueness is per tenant).
        var sku = TestData.UniqueSku("MT");
        var productA = (await ReadAsync<CreatedResponse>(
            await clientA.PostAsJsonAsync(Products, TestData.ValidCreateProductPayload(sku)), HttpStatusCode.Created)).Id;
        await ReadAsync<CreatedResponse>(
            await clientB.PostAsJsonAsync(Products, TestData.ValidCreateProductPayload(sku)), HttpStatusCode.Created);

        (await ReadAsync<ProductDto>(await clientA.GetAsync($"{Products}/{productA}"))).Sku.Should().Be(sku);

        // Tenant B cannot see, change or delete tenant A's product: 404, never 403 (no existence leak).
        (await clientB.GetAsync($"{Products}/{productA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await clientB.DeleteAsync($"{Products}/{productA}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tenant_resolution_rules_are_enforced()
    {
        var (tenantA, ownerA) = await CreateTenantAsync("rules");
        var (tenantB, _) = await CreateTenantAsync("other");

        // Tenant data without a tenant: 400 tenant_required.
        (await fixture.Client(ownerA).GetAsync($"{Products}/{Guid.NewGuid()}")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        // A session of tenant A plus a header naming tenant B: 403 tenant_mismatch.
        var mismatch = fixture.Client(ownerA, tenantA);
        mismatch.DefaultRequestHeaders.Add("X-Tenant", tenantB.ToString());
        (await mismatch.GetAsync("/api/v1/tenants/current")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A non-member claiming tenant A: 403.
        (await fixture.Client(Guid.NewGuid(), tenantA).GetAsync("/api/v1/tenants/current")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // The owner sees the tenant and it is listed as current in the switcher.
        var owner = fixture.Client(ownerA, tenantA);
        (await ReadAsync<TenantDto>(await owner.GetAsync("/api/v1/tenants/current"))).Id.Should().Be(tenantA);
        (await ReadAsync<List<UserTenantDto>>(await owner.GetAsync("/api/v1/me/tenants")))
            .Should().ContainSingle(t => t.TenantId == tenantA && t.IsCurrent && t.IsOwner);
    }

    [Fact]
    public async Task Invitations_add_members_with_scoped_branches()
    {
        var (tenant, ownerId) = await CreateTenantAsync("invite");
        var owner = fixture.Client(ownerId, tenant);

        // Branch tree: HQ (created with the tenant) → Cairo.
        var tree = await ReadAsync<List<BranchTreeNodeDto>>(await owner.GetAsync("/api/v1/branches"));
        var headOffice = tree.Single(b => b.IsHeadOffice);
        var cairo = (await ReadAsync<CreateBranchResult>(await owner.PostAsJsonAsync("/api/v1/branches",
            new { parentId = headOffice.Id, code = "CAI", name = "Cairo" }), HttpStatusCode.Created)).Id;

        // Invite a member restricted to Cairo.
        var email = $"m{Guid.NewGuid():N}@example.com";
        var invitation = await ReadAsync<InviteMemberResult>(await owner.PostAsJsonAsync("/api/v1/tenant-invitations",
            new { email, roleIds = Array.Empty<Guid>(), branches = new[] { new { branchId = cairo, includeDescendants = true } } }));

        var memberId = Guid.NewGuid();
        (await ReadAsync<AcceptInvitationResult>(await fixture.Client(memberId, email: email)
            .PostAsJsonAsync("/api/v1/tenant-invitations/accept", new { token = invitation.Token }))).TenantId.Should().Be(tenant);

        // The member sees only its branch subtree and cannot manage branches.
        var member = fixture.Client(memberId, tenant);
        var visible = await ReadAsync<List<BranchTreeNodeDto>>(await member.GetAsync("/api/v1/branches"));
        visible.Should().ContainSingle().Which.Id.Should().Be(cairo);
        (await member.PostAsJsonAsync("/api/v1/branches", new { parentId = cairo, code = "X", name = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Removing the member revokes access immediately.
        (await owner.DeleteAsync($"/api/v1/members/{memberId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetAsync("/api/v1/tenants/current")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json);
        return body!.Data!;
    }
}
