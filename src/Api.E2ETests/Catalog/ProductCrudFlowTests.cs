using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.E2ETests.Infrastructure;
using Catalog.Contracts.Products.Dtos;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Catalog;

/// <summary>
/// Reference e2e flow for the Catalog sample module: create → get → update → list → delete
/// over real HTTP against the booted Api + Postgres, authenticated as an Admin.
/// </summary>
[Collection(E2ECollection.Name)]
public class ProductCrudFlowTests(E2ETestFixture fixture)
{
    private const string BasePath = "/api/v1/products";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors);

    private sealed record CreatedResponse(Guid Id);

    private sealed record PageInfo(int CurrentPage, int PageSize, int TotalItems);

    private sealed record Page<T>(PageInfo PageInfoResult, List<T>? Result);

    private HttpClient Admin() => fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);

    [Fact]
    public async Task Admin_CanCreateGetUpdateListAndDeleteProduct()
    {
        var admin = Admin();
        var sku = TestData.UniqueSku("FLOW");

        // ── Create ──────────────────────────────────────────────────────────
        var createResponse = await admin.PostAsJsonAsync(
            BasePath, TestData.ValidCreateProductPayload(sku, price: 10.50m));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            await createResponse.Content.ReadAsStringAsync());
        var created = await createResponse.Content.ReadFromJsonAsync<Envelope<CreatedResponse>>(Json);
        var id = created!.Data!.Id;
        id.Should().NotBeEmpty();

        // ── Get by id (anonymous read) ──────────────────────────────────────
        var product = await GetProductAsync(fixture.CreateAnonymousClient(), id);
        product.Sku.Should().Be(sku);
        product.Price.Should().Be(10.50m);
        product.IsActive.Should().BeTrue();
        var loadedVersion = product.Version;

        // ── Update with the current version ─────────────────────────────────
        var updateResponse = await admin.PutAsJsonAsync($"{BasePath}/{id}", new
        {
            Name = "E2E Product (renamed)",
            Description = "Updated by the e2e suite.",
            Sku = sku,
            Price = 12.75m,
            IsActive = false,
            ImageAttachmentId = (Guid?)null,
            Version = loadedVersion,
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await updateResponse.Content.ReadAsStringAsync());

        var updated = await GetProductAsync(admin, id);
        updated.Name.Should().Be("E2E Product (renamed)");
        updated.Price.Should().Be(12.75m);
        updated.IsActive.Should().BeFalse();
        updated.Version.Should().BeGreaterThan(loadedVersion);

        // ── Update again with the now-stale version → 409 ───────────────────
        var staleResponse = await admin.PutAsJsonAsync($"{BasePath}/{id}", new
        {
            Name = "Should not apply",
            Description = (string?)null,
            Sku = sku,
            Price = 1m,
            IsActive = true,
            Version = loadedVersion,
        });
        staleResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // ── List with search ────────────────────────────────────────────────
        var listResponse = await fixture.CreateAnonymousClient()
            .GetAsync($"{BasePath}?search={sku}&pageNumber=1&pageSize=10");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            await listResponse.Content.ReadAsStringAsync());
        var page = await listResponse.Content.ReadFromJsonAsync<Envelope<Page<ProductDto>>>(Json);
        page!.Data!.Result.Should().ContainSingle(p => p.Id == id)
            .Which.Name.Should().Be("E2E Product (renamed)");
        page.Data.PageInfoResult.TotalItems.Should().Be(1);

        // ── Delete ──────────────────────────────────────────────────────────
        var deleteResponse = await admin.DeleteAsync($"{BasePath}/{id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterDelete = await admin.GetAsync($"{BasePath}/{id}");
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatingDuplicateSku_ReturnsConflict()
    {
        var admin = Admin();
        var sku = TestData.UniqueSku("DUP");

        (await admin.PostAsJsonAsync(BasePath, TestData.ValidCreateProductPayload(sku)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Same SKU, different casing — SKUs are normalised before the uniqueness check.
        var duplicate = await admin.PostAsJsonAsync(
            BasePath, TestData.ValidCreateProductPayload(sku.ToLowerInvariant()));

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Writes_RequireAdmin()
    {
        var payload = TestData.ValidCreateProductPayload();

        var anonymous = await fixture.CreateAnonymousClient().PostAsJsonAsync(BasePath, payload);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var plainUser = await fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User)
            .PostAsJsonAsync(BasePath, payload);
        plainUser.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreatingWithNegativePrice_ReturnsBadRequest()
    {
        var response = await Admin().PostAsJsonAsync(
            BasePath, TestData.ValidCreateProductPayload(price: -1m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<ProductDto> GetProductAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{BasePath}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Envelope<ProductDto>>(Json);
        return body!.Data!;
    }
}
