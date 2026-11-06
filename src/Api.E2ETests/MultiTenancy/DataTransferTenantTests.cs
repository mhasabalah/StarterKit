using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Tenants;
using Xunit;

namespace Api.E2ETests.MultiTenancy;

/// <summary>
/// Bulk import/export with <c>MultiTenancy:Enabled=true</c>: permissions come from REAL memberships and tenant roles,
/// the Hangfire data-transfer server runs the job, and a branch-restricted member's export contains only the products
/// of their branches (the job runs in the requester's branch scope, not the job server's unrestricted one).
/// </summary>
[Collection(MultiTenantE2ECollection.Name)]
public sealed class DataTransferTenantTests(MultiTenantE2EFixture fixture)
{
    private const string Api = "/api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Envelope<T>(bool IsSuccess, T? Data);

    private sealed record IdResult(Guid Id);

    private sealed record BranchNode(Guid Id, bool IsHeadOffice);

    private sealed record Job(Guid Id, int Status, long ProcessedRows, string? FailureCode);

    private sealed record JobDetail(Job Job);

    private const int Completed = 3;

    [Fact]
    public async Task A_branch_restricted_member_exports_only_their_branches_and_cannot_import_without_the_permission()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var owner = fixture.Client(ownerId, tenant);
        var headOffice = await HeadOfficeAsync(owner);
        var branch = await PostAsync<IdResult>(owner, $"{Api}/branches", new { parentId = headOffice, code = "B1", name = "Branch one" });
        var exporter = await PostAsync<IdResult>(owner, $"{Api}/tenant-roles", new
        {
            name = "Exporter",
            description = "Exports products",
            permissions = new[] { "datatransfer.export", "catalog.products.manage" }
        });
        var memberId = await AddMemberAsync(owner, exporter.Id, branch.Id);

        var inHeadOffice = await CreateProductAsync(owner);
        var inBranch = await CreateProductAsync(owner);
        await AssignBranchAsync(inHeadOffice, headOffice);
        await AssignBranchAsync(inBranch, branch.Id);

        var member = fixture.Client(memberId, tenant);
        var memberExport = await WaitAsync(member, await StartExportAsync(member));
        var ownerExport = await WaitAsync(owner, await StartExportAsync(owner));

        memberExport.Job.Status.Should().Be(Completed);
        memberExport.Job.ProcessedRows.Should().Be(1, "only the product of the member's branch");
        ownerExport.Job.ProcessedRows.Should().Be(2, "the owner sees every branch");

        (await StartImportAsync(member)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "no datatransfer.import");
    }

    [Fact]
    public async Task A_member_without_any_transfer_permission_is_refused_and_another_tenant_cannot_see_a_job()
    {
        var (tenant, ownerId) = await CreateTenantAsync();
        var owner = fixture.Client(ownerId, tenant);
        var plainMemberId = await AddMemberAsync(owner, roleId: null, branchId: await HeadOfficeAsync(owner));
        var plainMember = fixture.Client(plainMemberId, tenant);

        (await plainMember.PostAsJsonAsync($"{Api}/data-transfer/exports", new { definitionKey = "catalog.products" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StartImportAsync(plainMember)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var job = await StartExportAsync(owner);
        await WaitAsync(owner, job);

        var (otherTenant, otherOwnerId) = await CreateTenantAsync();
        var stranger = fixture.Client(otherOwnerId, otherTenant);
        (await stranger.GetAsync($"{Api}/data-transfer/jobs/{job}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"{Api}/data-transfer/jobs/{job}/files/Result")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(Guid TenantId, Guid OwnerId)> CreateTenantAsync()
    {
        var ownerId = Guid.NewGuid();
        var identifier = $"dt-{Guid.NewGuid():N}"[..20];
        using var response = await fixture.Client(ownerId).PostAsJsonAsync($"{Api}/tenants", new { name = "dt", identifier });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Envelope<CreateTenantResult>>(Json);
        return (body!.Data!.TenantId, ownerId);
    }

    private static async Task<Guid> HeadOfficeAsync(HttpClient owner)
    {
        using var tree = await owner.GetAsync($"{Api}/branches");
        return (await tree.Content.ReadFromJsonAsync<Envelope<List<BranchNode>>>(Json))!.Data!.Single(b => b.IsHeadOffice).Id;
    }

    private async Task<Guid> AddMemberAsync(HttpClient owner, Guid? roleId, Guid branchId)
    {
        var email = $"dt{Guid.NewGuid():N}@example.com";
        var invitation = await PostAsync<InviteMemberResult>(owner, $"{Api}/tenant-invitations", new
        {
            email,
            roleIds = roleId is { } id ? new[] { id } : Array.Empty<Guid>(),
            branches = new[] { new { branchId, includeDescendants = true } }
        }, HttpStatusCode.OK);
        var memberId = Guid.NewGuid();
        using var accept = await fixture.Client(memberId, email: email)
            .PostAsJsonAsync($"{Api}/tenant-invitations/accept", new { token = invitation.Token });
        accept.StatusCode.Should().Be(HttpStatusCode.OK, await accept.Content.ReadAsStringAsync());
        return memberId;
    }

    private static async Task<Guid> CreateProductAsync(HttpClient owner) =>
        (await PostAsync<IdResult>(owner, $"{Api}/products", new
        {
            name = new { en = "Branch product", ar = "منتج فرع" },
            sku = $"DT-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            type = 1,
            price = 10m,
            currency = 5
        })).Id;

    /// <summary>The API has no route that sets a product's branch: arrange it in the database.</summary>
    private async Task AssignBranchAsync(Guid productId, Guid branchId)
    {
        var connectionString = fixture.Factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("starterkit");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE catalog.products SET branch_id = @b WHERE id = @p", connection);
        command.Parameters.AddWithValue("b", branchId);
        command.Parameters.AddWithValue("p", productId);
        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private static async Task<Guid> StartExportAsync(HttpClient client) =>
        (await PostAsync<IdResult>(client, $"{Api}/data-transfer/exports", new { definitionKey = "catalog.products", format = 1 },
            HttpStatusCode.Accepted)).Id;

    private static Task<HttpResponseMessage> StartImportAsync(HttpClient client)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("sku,name_en,name_ar,type,price,currency\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "products.csv");
        form.Add(new StringContent("catalog.products"), "definitionKey");
        return client.PostAsync($"{Api}/data-transfer/imports", form);
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body,
        HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Envelope<T>>(Json))!.Data!;
    }

    private static async Task<JobDetail> WaitAsync(HttpClient client, Guid jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var detail = (await client.GetFromJsonAsync<Envelope<JobDetail>>($"{Api}/data-transfer/jobs/{jobId}", Json))!.Data!;
            if (detail.Job.Status >= Completed)
                return detail;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Job {jobId} is still {detail.Job.Status}.");
            await Task.Delay(250);
        }
    }
}
