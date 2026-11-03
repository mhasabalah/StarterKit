using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.E2ETests.Infrastructure;
using Catalog.Contracts.Categories;
using Catalog.Contracts.Products.Commands;
using Catalog.Contracts.Products.Dtos;
using Catalog.Contracts.Products.Enums;
using Catalog.Contracts.Products.Queries;
using Catalog.Contracts.Wishlist;
using Catalog.Data.Seed;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Catalog;

/// <summary>
/// Reference e2e flows for the Catalog module over real HTTP (booted Api + Postgres): the seller → moderation →
/// public catalogue lifecycle, resource authorization (403), optimistic concurrency (409), soft delete/restore,
/// wishlist and lookups. NOTE: the host auto-migrates, so these run green once the integrator has regenerated
/// the Catalog migration for the new model.
/// </summary>
[Collection(E2ECollection.Name)]
public class ProductCrudFlowTests(E2ETestFixture fixture)
{
    private const string Products = "/api/v1/products";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors);

    private sealed record CreatedResponse(Guid Id, bool ImagesAssociated);

    private sealed record VersionResponse(bool Success, long Version);

    private sealed record PageInfo(int CurrentPage, int PageSize, int TotalItems);

    private sealed record Page<T>(PageInfo PageInfoResult, List<T>? Result);

    private HttpClient Admin() => fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.Admin);

    private HttpClient Seller(Guid sellerId) => fixture.CreateClientAs(sellerId, RoleConstants.User);

    [Fact]
    public async Task SellerToPublicCatalogue_FullLifecycle()
    {
        var sellerId = Guid.NewGuid();
        var seller = Seller(sellerId);
        var admin = Admin();
        var anonymous = fixture.CreateAnonymousClient();
        var sku = TestData.UniqueSku("FLOW");

        // ── Seller creates a draft with tags + attributes ───────────────────
        var payload = TestData.ValidCreateProductPayload(sku, 80m, nameEn: $"Lamp {sku}");
        payload["compareAtPrice"] = 100m;
        payload["categoryId"] = CatalogDataSeeder.KitchenId;
        payload["tagIds"] = new[] { CatalogDataSeeder.NewArrivalTagId };
        payload["attributes"] = new[] { new { attributeTypeId = CatalogDataSeeder.ColorAttributeId, value = "Red" } };
        var id = (await ReadAsync<CreatedResponse>(await seller.PostAsJsonAsync(Products, payload),
            HttpStatusCode.Created)).Id;

        // Draft is invisible to the public, visible to its owner.
        (await anonymous.GetAsync($"{Products}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync<ProductDto>(await seller.GetAsync($"{Products}/{id}"))).Sku.Should().Be(sku);

        // ── Moderation: submit → approve → publish (versions chained) ───────
        var submitted = await ReadAsync<ProductStatusChangedResult>(
            await seller.PostAsJsonAsync($"{Products}/{id}/submit-for-review", new { }));
        submitted.Status.Should().Be(ProductStatus.PendingReview);

        var queue = await ReadAsync<Page<PendingProductDto>>(
            await admin.GetAsync("/api/v1/admin/products/pending?pageSize=100"));
        queue.Result.Should().Contain(p => p.Id == id);

        var approved = await ReadAsync<ProductStatusChangedResult>(await admin.PostAsJsonAsync(
            $"{Products}/{id}/approve", new { reviewNotes = "Looks good", version = submitted.Version }));
        var published = await ReadAsync<ProductStatusChangedResult>(await admin.PostAsJsonAsync(
            $"{Products}/{id}/publish", new { version = approved.Version }));
        published.Status.Should().Be(ProductStatus.Published);

        // ── Public list / search / detail ───────────────────────────────────
        var list = await ReadAsync<Page<ProductDto>>(await anonymous.GetAsync(
            $"{Products}?ownerId={sellerId}&tagIds={CatalogDataSeeder.NewArrivalTagId}&pageNumber=1&pageSize=10"));
        var listed = list.Result.Should().ContainSingle().Subject;
        listed.DiscountPercentage.Should().Be(20m);
        listed.CategoryName.Should().Be("Kitchen");

        var search = await ReadAsync<Page<ProductDto>>(await anonymous.GetAsync($"{Products}/search?searchTerm={sku}"));
        search.Result.Should().ContainSingle(p => p.Id == id);

        var detail = await ReadAsync<ProductDto>(await anonymous.GetAsync($"{Products}/{id}"));
        detail.ViewCount.Should().Be(1);
        detail.Attributes.Should().ContainSingle(a => a.Value == "Red");
        (await ReadAsync<ProductDto>(await anonymous.GetAsync($"{Products}/{id}"))).ViewCount.Should().Be(2);

        // ── Wishlist ────────────────────────────────────────────────────────
        var shopper = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);
        (await shopper.PostAsync($"{Products}/{id}/wishlist", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await shopper.PostAsync($"{Products}/{id}/wishlist", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadAsync<ProductDto>(await shopper.GetAsync($"{Products}/{id}"))).IsWishlisted.Should().BeTrue();
        (await ReadAsync<Page<WishlistItemDto>>(await shopper.GetAsync($"{Products}/wishlist")))
            .Result.Should().ContainSingle(w => w.ProductId == id && w.IsAvailable);

        // ── Owner archives; the product leaves the public catalogue ─────────
        var archived = await ReadAsync<ProductStatusChangedResult>(
            await seller.PostAsJsonAsync($"{Products}/{id}/archive", new { }));
        archived.Status.Should().Be(ProductStatus.Archived);
        (await anonymous.GetAsync($"{Products}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var mine = await ReadAsync<GetMyProductsResult>(await seller.GetAsync($"{Products}/my"));
        mine.Stats.Archived.Should().Be(1);
        // 2 anonymous views + 1 by the shopper (any non-owner counts); the seller's own reads never count.
        mine.Stats.TotalViews.Should().Be(3);
    }

    [Fact]
    public async Task NonOwner_CannotEditOrDelete_AndOwnerCannotEditWhilePending()
    {
        var sellerId = Guid.NewGuid();
        var id = await CreateAsync(Seller(sellerId));
        var stranger = Seller(Guid.NewGuid());

        (await stranger.PutAsJsonAsync($"{Products}/{id}", TestData.ValidCreateProductPayload()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.DeleteAsync($"{Products}/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostAsJsonAsync($"{Products}/{id}/submit-for-review", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var owner = Seller(sellerId);
        (await owner.PostAsJsonAsync($"{Products}/{id}/submit-for-review", new { })).EnsureSuccessStatusCode();
        (await owner.PutAsJsonAsync($"{Products}/{id}", TestData.ValidCreateProductPayload()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "pending products are locked for their owner");

        // Moderation endpoints are admin-only.
        (await owner.PostAsJsonAsync($"{Products}/{id}/approve", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StaleVersion_Returns409()
    {
        var seller = Seller(Guid.NewGuid());
        var id = await CreateAsync(seller);
        var version = (await ReadAsync<ProductDto>(await seller.GetAsync($"{Products}/{id}"))).Version;

        var first = TestData.ValidCreateProductPayload(nameEn: "First writer");
        first["version"] = version;
        var updated = await ReadAsync<VersionResponse>(await seller.PutAsJsonAsync($"{Products}/{id}", first));
        updated.Version.Should().Be(version + 1);

        var stale = TestData.ValidCreateProductPayload(nameEn: "Lost update");
        stale["version"] = version;
        (await seller.PutAsJsonAsync($"{Products}/{id}", stale)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await ReadAsync<ProductDto>(await seller.GetAsync($"{Products}/{id}"))).Name.Should().Be("First writer");
    }

    [Fact]
    public async Task SoftDelete_ThenAdminRestore()
    {
        var seller = Seller(Guid.NewGuid());
        var admin = Admin();
        var id = await CreateAsync(seller);

        (await seller.DeleteAsync($"{Products}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await seller.GetAsync($"{Products}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var deleted = await ReadAsync<ProductAdminDto>(await admin.GetAsync($"/api/v1/admin/products/{id}"));
        deleted.IsDeleted.Should().BeTrue();

        (await seller.PostAsync($"/api/v1/admin/products/{id}/restore", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "restore is admin-only");
        (await admin.PostAsync($"/api/v1/admin/products/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await seller.GetAsync($"{Products}/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Writes_RequireAuthentication_LookupsRequireAdmin()
    {
        (await fixture.CreateAnonymousClient().PostAsJsonAsync(Products, TestData.ValidCreateProductPayload()))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var user = Seller(Guid.NewGuid());
        (await user.PostAsJsonAsync("/api/v1/tags", new { name = new { en = "x", ar = "س" } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await user.GetAsync("/api/v1/admin/products")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DuplicateSku_Returns409_AndInvalidPayload_Returns400()
    {
        var seller = Seller(Guid.NewGuid());
        var sku = TestData.UniqueSku("DUP");
        (await seller.PostAsJsonAsync(Products, TestData.ValidCreateProductPayload(sku)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await seller.PostAsJsonAsync(Products, TestData.ValidCreateProductPayload(sku.ToLowerInvariant())))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await seller.PostAsJsonAsync(Products, TestData.ValidCreateProductPayload(price: -1m)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CategoryTree_IsPublicAndLocalized()
    {
        var anonymous = fixture.CreateAnonymousClient();

        var english = await ReadAsync<List<CategoryTreeNodeDto>>(await anonymous.GetAsync("/api/v1/categories/tree"));
        english.Should().Contain(c => c.Id == CatalogDataSeeder.ElectronicsId && c.Name == "Electronics"
                                      && c.Children.Any(child => child.Id == CatalogDataSeeder.PhonesId));

        var arabic = await ReadAsync<List<CategoryTreeNodeDto>>(
            await anonymous.GetAsync("/api/v1/categories/tree?culture=ar&ui-culture=ar"));
        arabic.Single(c => c.Id == CatalogDataSeeder.ElectronicsId).Name.Should().Be("إلكترونيات");
    }

    private static async Task<Guid> CreateAsync(HttpClient client) =>
        (await ReadAsync<CreatedResponse>(await client.PostAsJsonAsync(Products, TestData.ValidCreateProductPayload()),
            HttpStatusCode.Created)).Id;

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json);
        return body!.Data!;
    }
}
