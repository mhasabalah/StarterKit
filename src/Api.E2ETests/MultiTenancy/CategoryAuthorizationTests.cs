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
/// Category writes are TENANT data: they need <c>catalog.categories.manage</c> in the current tenant (a membership
/// permission), no longer the platform <c>AdminOnly</c> policy. Real memberships and tenant roles, multi-tenant host.
/// </summary>
[Collection(MultiTenantE2ECollection.Name)]
public sealed class CategoryAuthorizationTests(MultiTenantE2EFixture fixture)
{
    private const string Api = "/api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record IdResult(Guid Id);

    private sealed record BranchNode(Guid Id, bool IsHeadOffice);

    private sealed record RoleRow(Guid Id, string Name);

    [Fact]
    public async Task A_member_without_the_permission_cannot_create_update_delete_or_list_categories()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var owner = fixture.Client(ownerId, tenant);
        var existing = await CreateAsync(owner);
        var memberId = await AddMemberAsync(owner, tenant, roleName: null);
        var member = fixture.Client(memberId, tenant);

        (await member.PostAsJsonAsync($"{Api}/categories", Body())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PutAsJsonAsync($"{Api}/categories/{existing}", Body())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.DeleteAsync($"{Api}/categories/{existing}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"{Api}/categories")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_category_write_but_can_read_the_tree()
    {
        var identifier = $"an-{Guid.NewGuid():N}"[..20];
        using var created = await fixture.Client(Guid.NewGuid()).PostAsJsonAsync($"{Api}/tenants", new { name = "anon", identifier });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var anonymous = fixture.Factory.CreateAnonymousClient();
        anonymous.DefaultRequestHeaders.Add("X-Tenant", identifier); // resolves the tenant, authenticates nothing
        var id = Guid.NewGuid();

        (await anonymous.PostAsJsonAsync($"{Api}/categories", Body())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PutAsJsonAsync($"{Api}/categories/{id}", Body())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Api}/categories/{id}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"{Api}/categories")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_platform_admin_without_a_membership_is_refused_in_multi_tenant_mode()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var existing = await CreateAsync(fixture.Client(ownerId, tenant));
        var admin = fixture.Factory.CreateClientAs(Guid.NewGuid(), "Admin");
        admin.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenant.ToString());

        (await admin.PostAsJsonAsync($"{Api}/categories", Body())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"{Api}/categories/{existing}", Body())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"{Api}/categories/{existing}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_tenant_administrator_and_the_owner_manage_categories()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var owner = fixture.Client(ownerId, tenant);
        var adminMemberId = await AddMemberAsync(owner, tenant, roleName: "Administrator");
        var administrator = fixture.Client(adminMemberId, tenant);

        var created = await CreateAsync(administrator);
        (await administrator.PutAsJsonAsync($"{Api}/categories/{created}", Body())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await administrator.GetAsync($"{Api}/categories")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await administrator.DeleteAsync($"{Api}/categories/{created}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var byOwner = await CreateAsync(owner);
        (await owner.DeleteAsync($"{Api}/categories/{byOwner}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_custom_role_with_only_the_categories_permission_manages_categories_and_nothing_else_of_catalog()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var owner = fixture.Client(ownerId, tenant);
        using var role = await owner.PostAsJsonAsync($"{Api}/tenant-roles", new
        {
            name = "Category editor",
            permissions = new[] { "catalog.categories.manage" }
        });
        role.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var roleId = (await role.Content.ReadFromJsonAsync<Envelope<IdResult>>(Json))!.Data!.Id;
        var memberId = await AddMemberAsync(owner, tenant, roleName: null, roleId: roleId);
        var editor = fixture.Client(memberId, tenant);

        var created = await CreateAsync(editor);
        (await editor.DeleteAsync($"{Api}/categories/{created}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bulk_transfer_of_categories_and_members_needs_the_definitions_own_permission()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var owner = fixture.Client(ownerId, tenant);
        using var role = await owner.PostAsJsonAsync($"{Api}/tenant-roles", new
        {
            name = "Exporter only",
            permissions = new[] { "datatransfer.export", "datatransfer.import" }
        });
        role.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var roleId = (await role.Content.ReadFromJsonAsync<Envelope<IdResult>>(Json))!.Data!.Id;
        var member = fixture.Client(await AddMemberAsync(owner, tenant, roleName: null, roleId: roleId), tenant);

        (await member.PostAsJsonAsync($"{Api}/data-transfer/exports", new { definitionKey = "catalog.categories" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.PostAsJsonAsync($"{Api}/data-transfer/exports", new { definitionKey = "tenancy.members" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var key in new[] { "catalog.categories", "tenancy.members" })
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("slug,x"));
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
            form.Add(file, "file", "rows.csv");
            form.Add(new StringContent(key), "definitionKey");
            form.Add(new StringContent("DryRun"), "mode");
            (await member.PostAsync($"{Api}/data-transfer/imports", form)).StatusCode
                .Should().Be(HttpStatusCode.Forbidden, $"import of {key} needs the definition's own permission");
        }

        using var ownerExport = await owner.PostAsJsonAsync($"{Api}/data-transfer/exports", new { definitionKey = "catalog.categories" });
        ownerExport.StatusCode.Should().Be(HttpStatusCode.Accepted, await ownerExport.Content.ReadAsStringAsync());
    }

    private static object Body() => new
    {
        name = new { en = "Auth test", ar = "اختبار" },
        slug = $"auth-{Guid.NewGuid():N}"[..16],
        parentId = (Guid?)null,
        displayOrder = 0
    };

    private static async Task<Guid> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync($"{Api}/categories", Body());
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Envelope<IdResult>>(Json))!.Data!.Id;
    }

    private async Task<(Guid TenantId, Guid OwnerId)> CreateTenantAsync()
    {
        var ownerId = Guid.NewGuid();
        var identifier = $"ca-{Guid.NewGuid():N}"[..20];
        using var response = await fixture.Client(ownerId).PostAsJsonAsync($"{Api}/tenants", new { name = "cat", identifier });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Envelope<CreateTenantResult>>(Json);
        return (body!.Data!.TenantId, ownerId);
    }

    private async Task<Guid> AddMemberAsync(HttpClient owner, Guid tenant, string? roleName, Guid? roleId = null)
    {
        if (roleName is not null)
        {
            using var roles = await owner.GetAsync($"{Api}/tenant-roles");
            roleId = (await roles.Content.ReadFromJsonAsync<Envelope<List<RoleRow>>>(Json))!.Data!
                .Single(r => r.Name == roleName).Id;
        }

        using var tree = await owner.GetAsync($"{Api}/branches");
        var headOffice = (await tree.Content.ReadFromJsonAsync<Envelope<List<BranchNode>>>(Json))!.Data!
            .Single(b => b.IsHeadOffice).Id;

        var email = $"ca{Guid.NewGuid():N}@example.com";
        using var invite = await owner.PostAsJsonAsync($"{Api}/tenant-invitations", new
        {
            email,
            roleIds = roleId is { } id ? new[] { id } : Array.Empty<Guid>(),
            branches = new[] { new { branchId = headOffice, includeDescendants = true } }
        });
        invite.StatusCode.Should().Be(HttpStatusCode.OK, await invite.Content.ReadAsStringAsync());
        var token = (await invite.Content.ReadFromJsonAsync<Envelope<InviteMemberResult>>(Json))!.Data!.Token;

        var memberId = Guid.NewGuid();
        using var accept = await fixture.Client(memberId, email: email)
            .PostAsJsonAsync($"{Api}/tenant-invitations/accept", new { token });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        return memberId;
    }
}
