using System.Globalization;
using Catalog.Contracts.Products.Commands;
using Catalog.Contracts.Products.Enums;
using Catalog.Contracts.Products.Queries;
using Catalog.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Enums;

namespace Catalog.IntegrationTests;

[Collection(CatalogPostgresCollection.Name)]
public sealed class ProductQueryTests(CatalogPostgresFixture fx)
{
    private readonly Scenario _s = new(fx);

    private Task<Shared.Contracts.Pagination.PaginatedResult<Contracts.Products.Dtos.ProductDto>> Public(
        GetProductsQuery query, Guid? asUser = null) =>
        fx.SendAs(asUser, query with { OwnerId = query.OwnerId ?? _s.Owner, PageSize = query.PageSize ?? 50 });

    [Fact]
    public async Task PublicList_ReturnsOnlyPublished_EvenWhenAnotherStatusIsRequested()
    {
        var published = await _s.Published("Visible");
        await _s.Create("Draft one");

        var page = await Public(new GetProductsQuery { Status = ProductStatus.Draft });

        page.Result!.Select(p => p.Id).Should().Equal(published);
        page.PageInfoResult.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task AdminList_SeesEveryStatus_WithRawBilingualName()
    {
        var draft = await _s.Create("Draft", "مسودة");
        await _s.Published("Live");

        var page = await fx.SendAsAdmin(new GetAdminProductsQuery { OwnerId = _s.Owner, Status = ProductStatus.Draft });

        var dto = page.Result!.Should().ContainSingle().Subject;
        dto.Id.Should().Be(draft);
        dto.LocalizedName.Ar.Should().Be("مسودة");
        dto.StatusLabel.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Tags_UseHasAllSemantics()
    {
        var both = await _s.Published("Both",
            tags: [CatalogDataSeeder.NewArrivalTagId, CatalogDataSeeder.BestSellerTagId]);
        await _s.Published("One", tags: [CatalogDataSeeder.NewArrivalTagId]);

        var page = await Public(new GetProductsQuery
        {
            TagIds = [CatalogDataSeeder.NewArrivalTagId, CatalogDataSeeder.BestSellerTagId]
        });

        page.Result!.Select(p => p.Id).Should().Equal(both);
        page.Result![0].Tags.Select(t => t.Name).Should().BeEquivalentTo(["New Arrival", "Best Seller"]);
    }

    [Fact]
    public async Task Attributes_UseHasAllSemantics_AndAreProjectedWithTheirType()
    {
        var full = await _s.Published("Full", attributes:
        [
            new ProductAttributeInput(CatalogDataSeeder.ColorAttributeId, "Red"),
            new ProductAttributeInput(CatalogDataSeeder.WeightAttributeId, "1.5")
        ]);
        await _s.Published("ColorOnly", attributes: [new ProductAttributeInput(CatalogDataSeeder.ColorAttributeId, "Blue")]);

        var page = await Public(new GetProductsQuery
        {
            AttributeTypeIds = [CatalogDataSeeder.ColorAttributeId, CatalogDataSeeder.WeightAttributeId]
        });

        var dto = page.Result!.Should().ContainSingle().Subject;
        dto.Id.Should().Be(full);
        dto.Attributes.Should().Contain(a => a.Name == "Weight" && a.Unit == "kg" && a.Value == "1.5");
    }

    [Fact]
    public async Task Category_MatchesDescendantsOnlyWhenAsked()
    {
        var phone = await _s.Published("Phone", categoryId: CatalogDataSeeder.PhonesId);

        (await Public(new GetProductsQuery { CategoryId = CatalogDataSeeder.ElectronicsId })).Result.Should().BeEmpty();

        var withChildren = await Public(new GetProductsQuery
        {
            CategoryId = CatalogDataSeeder.ElectronicsId, IncludeSubcategories = true
        });
        withChildren.Result!.Single().Id.Should().Be(phone);
        withChildren.Result!.Single().CategoryName.Should().Be("Phones");
    }

    [Fact]
    public async Task PriceRange_Currency_Type_AndFeatured_Filters()
    {
        var cheap = await _s.Published("Cheap", price: 5m);
        var mid = await _s.Published("Mid", price: 50m, currency: Currency.EUR);
        var service = await _s.Published("Service", price: 500m, type: ProductType.Service);
        await fx.SendAsAdmin(new ToggleProductFeaturedCommand(mid, true));

        (await Public(new GetProductsQuery { MinPrice = 10, MaxPrice = 100 })).Result!.Select(p => p.Id)
            .Should().Equal(mid);
        (await Public(new GetProductsQuery { Currency = Currency.USD })).Result!.Select(p => p.Id)
            .Should().BeEquivalentTo([cheap, service]);
        (await Public(new GetProductsQuery { Type = ProductType.Service })).Result!.Select(p => p.Id)
            .Should().Equal(service);
        (await Public(new GetProductsQuery { IsFeatured = true })).Result!.Select(p => p.Id).Should().Equal(mid);
    }

    [Fact]
    public async Task Search_EscapesLikeWildcards()
    {
        var percent = await _s.Published("Save 50% today");
        await _s.Published("Size 500 ml");
        var underscore = await _s.Published("snake_case mug");
        await _s.Published("snakeXcase mug");

        (await Public(new GetProductsQuery { Search = "50%" })).Result!.Select(p => p.Id).Should().Equal(percent);
        (await Public(new GetProductsQuery { Search = "e_c" })).Result!.Select(p => p.Id).Should().Equal(underscore);
    }

    [Fact]
    public async Task Search_MatchesArabicNames_SkuAndDescription_CaseInsensitively()
    {
        var id = await _s.Published("Lamp", description: "Warm LIGHT for reading");

        (await Public(new GetProductsQuery { Search = "light" })).Result!.Select(p => p.Id).Should().Equal(id);
        (await Public(new GetProductsQuery { Search = "LAMP" })).Result!.Select(p => p.Id).Should().Equal(id);
        (await Public(new GetProductsQuery { Search = "أداة" })).Result!.Select(p => p.Id).Should().Equal(id);
    }

    [Fact]
    public async Task SearchEndpointQuery_FindsPublishedOnly()
    {
        var token = $"tok{Guid.NewGuid():N}"[..12];
        var live = await _s.Published($"Live {token}");
        await _s.Create($"Draft {token}");

        var page = await fx.SendAs(null, new SearchProductsQuery { SearchTerm = token });

        page.Result!.Select(p => p.Id).Should().Equal(live);
    }

    [Fact]
    public async Task Pagination_AndDynamicSort()
    {
        var a = await _s.Published("A", price: 30m);
        var b = await _s.Published("B", price: 10m);
        var c = await _s.Published("C", price: 20m);

        var page1 = await Public(new GetProductsQuery { SortColumn = "price", SortBy = SortBy.Ascending, PageSize = 2 });
        var page2 = await Public(new GetProductsQuery
        {
            SortColumn = "price", SortBy = SortBy.Ascending, PageSize = 2, PageNumber = 2
        });

        page1.Result!.Select(p => p.Id).Should().Equal(b, c);
        page2.Result!.Select(p => p.Id).Should().Equal(a);
        page1.PageInfoResult.TotalItems.Should().Be(3);

        var defaultOrder = await Public(new GetProductsQuery());
        defaultOrder.Result!.Select(p => p.Id).Should().Equal(c, b, a); // newest first

        var unknownColumn = await Public(new GetProductsQuery { SortColumn = "drop table" });
        unknownColumn.Result!.Select(p => p.Id).Should().Equal(c, b, a);
    }

    [Fact]
    public async Task Names_AreResolvedForTheRequestCulture()
    {
        var id = await _s.Published("Chair");
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar");
            var dto = (await Public(new GetProductsQuery())).Result!.Single(p => p.Id == id);
            dto.Name.Should().Be("أداة Chair");
            dto.TypeLabel.Should().Be("منتج مادي");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task PublicDetail_OwnerSeesDraft_OthersGet404()
    {
        var draft = await _s.Create("Secret");

        (await fx.SendAs(_s.Owner, new GetProductByIdQuery(draft))).Product.Id.Should().Be(draft);

        var stranger = () => fx.SendAs(Guid.NewGuid(), new GetProductByIdQuery(draft));
        await stranger.Should().ThrowAsync<Catalog.Domains.Products.Exceptions.ProductNotFoundException>();

        var adminDetail = await fx.SendAsAdmin(new GetAdminProductByIdQuery(draft));
        adminDetail.Product.Status.Should().Be(ProductStatus.Draft);
    }

    [Fact]
    public async Task ViewCounter_IncrementsForVisitors_NotForTheOwner_NorWhenDisabled()
    {
        var id = await _s.Published("Counted");

        (await fx.SendAs(null, new GetProductByIdQuery(id))).Product.ViewCount.Should().Be(1);
        (await fx.SendAs(Guid.NewGuid(), new GetProductByIdQuery(id))).Product.ViewCount.Should().Be(2);
        (await fx.SendAs(_s.Owner, new GetProductByIdQuery(id))).Product.ViewCount.Should().Be(2);
        (await fx.SendAs(null, new GetProductByIdQuery(id, IncrementView: false))).Product.ViewCount.Should().Be(2);

        var version = await fx.WithDb(db => db.Products.Where(p => p.Id == id).Select(p => p.Version).SingleAsync());
        var versionAfter = await fx.WithDb(async db =>
        {
            await db.Products.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1));
            return await db.Products.Where(p => p.Id == id).Select(p => p.Version).SingleAsync();
        });
        versionAfter.Should().Be(version, "counting views must never cause 409s for editors");
    }

    [Fact]
    public async Task ViewCounter_IsAtomicUnderConcurrency()
    {
        var id = await _s.Published("Busy");
        const int readers = 25;

        await Task.WhenAll(Enumerable.Range(0, readers).Select(_ => fx.WithDb(db =>
            db.Products.Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ViewCount, p => p.ViewCount + 1)))));

        (await fx.WithDb(db => db.Products.Where(p => p.Id == id).Select(p => p.ViewCount).SingleAsync()))
            .Should().Be(readers);
    }

    [Fact]
    public async Task MyProducts_ReturnsOnlyMine_WithExactStats()
    {
        var published = await _s.Published("P1");
        await _s.Create("D1");
        var pending = await _s.Create("Pending");
        await fx.SendAs(_s.Owner, new SubmitProductForReviewCommand(pending));
        var rejected = await _s.Create("Rejected");
        await fx.SendAs(_s.Owner, new SubmitProductForReviewCommand(rejected));
        await fx.SendAsAdmin(new RejectProductCommand(rejected, "Images are blurry"));
        await new Scenario(fx).Published("Someone else's");
        await fx.SendAs(null, new GetProductByIdQuery(published));

        var result = await fx.SendAs(_s.Owner, new GetMyProductsQuery { PageSize = 50 });

        result.Stats.Should().Be(new Contracts.Products.Dtos.MyProductStatsDto(
            Total: 4, Drafts: 1, InReview: 1, NeedsAttention: 1, Published: 1, Archived: 0, TotalViews: 1));
        result.Products.Result.Should().HaveCount(4).And.OnlyContain(p => p.OwnerId == _s.Owner);
        result.Products.Result!.Single(p => p.Id == rejected).ReviewNotes.Should().Be("Images are blurry");

        var filtered = await fx.SendAs(_s.Owner, new GetMyProductsQuery { Status = ProductStatus.Draft });
        filtered.Products.Result.Should().ContainSingle();
        filtered.Stats.Total.Should().Be(4, "stats ignore the list filters");
    }

    [Fact]
    public async Task ModerationQueue_IsFifo()
    {
        var first = await _s.Create("First");
        var second = await _s.Create("Second");
        await fx.SendAs(_s.Owner, new SubmitProductForReviewCommand(first));
        await fx.SendAs(_s.Owner, new SubmitProductForReviewCommand(second));

        var queue = await fx.SendAsAdmin(new GetPendingProductsQuery { PageSize = 100 });

        var ids = queue.Result!.Select(p => p.Id).ToList();
        ids.IndexOf(first).Should().BeLessThan(ids.IndexOf(second));
        queue.Result!.Should().OnlyContain(p => p.Status == ProductStatus.PendingReview);
    }

    [Fact]
    public async Task ProductStats_CountsAcrossTheModule()
    {
        var before = await fx.SendAsAdmin(new GetProductStatsQuery());
        await _s.Published("Stat");

        var after = await fx.SendAsAdmin(new GetProductStatsQuery());

        after.TotalProducts.Should().Be(before.TotalProducts + 1);
        after.PublishedProducts.Should().Be(before.PublishedProducts + 1);
    }
}
