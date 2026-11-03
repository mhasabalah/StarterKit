using System.Globalization;
using Catalog.Contracts.AttributeTypes;
using Catalog.Contracts.AttributeTypes.Enums;
using Catalog.Contracts.Categories;
using Catalog.Contracts.Products.Enums;
using Catalog.Contracts.Tags;
using Catalog.Data.Seed;
using Catalog.Domains.Categories.Cache;
using Catalog.Domains.Categories.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Localization;
using Shared.Core.Exceptions;

namespace Catalog.IntegrationTests;

[Collection(CatalogPostgresCollection.Name)]
public sealed class LookupTests(CatalogPostgresFixture fx)
{
    private static LocalizedText Name(string en) => LocalizedText.Create(en, "ع " + en);

    private static string Slug() => $"cat-{Guid.NewGuid():N}"[..20];

    private async Task<List<CategoryTreeNodeDto>> Tree() => await fx.SendAs(null, new GetCategoryTreeQuery());

    private static IEnumerable<CategoryTreeNodeDto> Flatten(IEnumerable<CategoryTreeNodeDto> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    [Fact]
    public async Task CategoryTree_IsCached_AndInvalidatedByCategoryChanges()
    {
        await Tree(); // warm the cache
        await fx.WithScope(async sp =>
            (await sp.GetRequiredService<ICategoryTreeCache>().GetAsync()).Should().NotBeNull());

        var created = await fx.SendAsAdmin(new CreateCategoryCommand(Name("Garden"), Slug(), CatalogDataSeeder.HomeId));

        // CategoryChangedEvent was dispatched after commit by the pipeline → cache invalidated → fresh tree.
        var tree = await Tree();
        var home = tree.Single(n => n.Id == CatalogDataSeeder.HomeId);
        home.Children.Should().Contain(c => c.Id == created.Id && c.Name == "Garden");

        await fx.SendAsAdmin(new DeleteCategoryCommand(created.Id));
        Flatten(await Tree()).Should().NotContain(c => c.Id == created.Id, "deactivated categories leave the tree");
    }

    [Fact]
    public async Task CategoryTree_IsLocalized()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ar");
            (await Tree()).Single(n => n.Id == CatalogDataSeeder.ElectronicsId).Name.Should().Be("إلكترونيات");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task UpdateCategory_RejectsCycles_AndUnknownParents()
    {
        var parent = await fx.SendAsAdmin(new CreateCategoryCommand(Name("P"), Slug()));
        var child = await fx.SendAsAdmin(new CreateCategoryCommand(Name("C"), Slug(), parent.Id));
        var parentSlug = await fx.WithDb(db => db.Categories.Where(c => c.Id == parent.Id).Select(c => c.Slug).SingleAsync());

        var cycle = () => fx.SendAsAdmin(new UpdateCategoryCommand(parent.Id, Name("P"), parentSlug, child.Id, 0));
        await cycle.Should().ThrowAsync<InvalidCategoryException>().WithMessage("*descendants*");

        var unknown = () => fx.SendAsAdmin(new UpdateCategoryCommand(parent.Id, Name("P"), parentSlug, Guid.NewGuid(), 0));
        await unknown.Should().ThrowAsync<InvalidCategoryException>();
    }

    [Fact]
    public async Task Category_DuplicateSlug_Is409()
    {
        var slug = Slug();
        await fx.SendAsAdmin(new CreateCategoryCommand(Name("A"), slug));

        var act = () => fx.SendAsAdmin(new CreateCategoryCommand(Name("B"), slug.ToUpperInvariant()));

        await act.Should().ThrowAsync<CategorySlugAlreadyExistsException>();
    }

    [Fact]
    public async Task Categories_AdminList_UsesMapsterProjection_WithRawAndDisplayNames()
    {
        var list = await fx.SendAsAdmin(new GetCategoriesQuery());

        var phones = list.Single(c => c.Id == CatalogDataSeeder.PhonesId);
        phones.Name.Should().Be(LocalizedText.Create("Phones", "هواتف"));
        phones.DisplayName.Should().Be("Phones");
        phones.ParentId.Should().Be(CatalogDataSeeder.ElectronicsId);
    }

    [Fact]
    public async Task Tags_DeactivateInsteadOfDelete_AndIncludeInactiveFilter()
    {
        var created = await fx.SendAsAdmin(new CreateTagCommand(Name($"tag-{Guid.NewGuid():N}"[..20])));

        await fx.SendAsAdmin(new DeleteTagCommand(created.Id));

        (await fx.SendAs(null, new GetTagsQuery())).Should().NotContain(t => t.Id == created.Id);
        var all = await fx.SendAsAdmin(new GetTagsQuery(IncludeInactive: true));
        all.Single(t => t.Id == created.Id).IsActive.Should().BeFalse();
        (await fx.WithDb(db => db.Tags.AnyAsync(t => t.Id == created.Id))).Should().BeTrue("never hard-deleted");
    }

    [Fact]
    public async Task Tag_UniqueName_Is409_AndStaleVersionIsRejected()
    {
        var name = Name($"uniq-{Guid.NewGuid():N}"[..20]);
        var created = await fx.SendAsAdmin(new CreateTagCommand(name));

        await FluentActions.Invoking(() => fx.SendAsAdmin(new CreateTagCommand(name)))
            .Should().ThrowAsync<ConflictException>();

        var updated = await fx.SendAsAdmin(new UpdateTagCommand(created.Id, Name("renamed-" + Guid.NewGuid().ToString("N")[..8]), Version: 0));
        updated.Version.Should().Be(1);
        await FluentActions.Invoking(() => fx.SendAsAdmin(new UpdateTagCommand(created.Id, name, Version: 0)))
            .Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task AttributeTypes_FilterByApplicableProductType()
    {
        var forService = await fx.SendAs(null, new GetAttributeTypesQuery(ProductType: ProductType.Service));
        forService.Select(a => a.Id).Should().Contain(CatalogDataSeeder.WarrantyMonthsAttributeId)
            .And.NotContain(CatalogDataSeeder.ColorAttributeId);

        var color = (await fx.SendAs(null, new GetAttributeTypesQuery()))
            .Single(a => a.Id == CatalogDataSeeder.ColorAttributeId);
        color.ApplicableTypes.Should().Equal(ProductType.Physical);
        color.DataType.Should().Be(AttributeDataType.Text);
        color.DataTypeLabel.Should().Be("Text");
    }

    [Fact]
    public async Task AttributeType_CreateUpdateDeactivate()
    {
        var created = await fx.SendAsAdmin(new CreateAttributeTypeCommand(
            Name($"attr-{Guid.NewGuid():N}"[..20]), AttributeDataType.Boolean));

        var updated = await fx.SendAsAdmin(new UpdateAttributeTypeCommand(created.Id, Name($"attr2-{Guid.NewGuid():N}"[..20]),
            AttributeDataType.Number, "cm", [ProductType.Physical]));
        updated.Success.Should().BeTrue();

        await fx.SendAsAdmin(new DeleteAttributeTypeCommand(created.Id));
        (await fx.SendAs(null, new GetAttributeTypesQuery())).Should().NotContain(a => a.Id == created.Id);
    }

    [Fact]
    public async Task Seeder_IsIdempotent()
    {
        var before = await fx.WithDb(async db => (await db.Categories.CountAsync(), await db.Tags.CountAsync(),
            await db.AttributeTypes.CountAsync()));

        await fx.WithScope(sp => sp.GetServices<Shared.Core.Data.Seed.IDataSeeder>().OfType<CatalogDataSeeder>()
            .Single().SeedAllAsync());
        await fx.WithScope(sp => sp.GetServices<Shared.Core.Data.Seed.IDataSeeder>().OfType<CatalogDataSeeder>()
            .Single().SeedAllAsync());

        var after = await fx.WithDb(async db => (await db.Categories.CountAsync(), await db.Tags.CountAsync(),
            await db.AttributeTypes.CountAsync()));
        after.Should().Be(before);
        (await fx.WithDb(db => db.Categories.AnyAsync(c => c.Id == CatalogDataSeeder.PhonesId))).Should().BeTrue();
    }
}
