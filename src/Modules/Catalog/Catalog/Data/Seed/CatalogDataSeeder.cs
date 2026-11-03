using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using Catalog.Domains.Categories.Cache;

namespace Catalog.Data.Seed;

/// <summary>
/// Idempotent sample lookups, PER TENANT: run at startup for the Default tenant by UseMigration&lt;CatalogDbContext&gt;()
/// and by tenant provisioning for every new tenant. Each set is seeded only when the tenant's table is empty —
/// <c>IgnoreNonIsolationFilters()</c> so soft-deleted rows also count — with fixed ids (the Default tenant keeps the
/// constants below; other tenants get stable derived ids, see <see cref="TenantSeedIds"/>).
/// Replace the samples with your own (or delete this seeder).
/// </summary>
public class CatalogDataSeeder(
    CatalogDbContext context,
    ITenantContext tenantContext,
    ICategoryTreeCache? categoryCache = null) : IDataSeeder
{
    public static readonly Guid ElectronicsId = new("30000000-0000-0000-0000-000000000001");
    public static readonly Guid PhonesId = new("30000000-0000-0000-0000-000000000002");
    public static readonly Guid LaptopsId = new("30000000-0000-0000-0000-000000000003");
    public static readonly Guid HomeId = new("30000000-0000-0000-0000-000000000004");
    public static readonly Guid KitchenId = new("30000000-0000-0000-0000-000000000005");
    public static readonly Guid ServicesId = new("30000000-0000-0000-0000-000000000006");

    public static readonly Guid NewArrivalTagId = new("40000000-0000-0000-0000-000000000001");
    public static readonly Guid BestSellerTagId = new("40000000-0000-0000-0000-000000000002");
    public static readonly Guid EcoFriendlyTagId = new("40000000-0000-0000-0000-000000000003");
    public static readonly Guid LimitedEditionTagId = new("40000000-0000-0000-0000-000000000004");

    public static readonly Guid ColorAttributeId = new("50000000-0000-0000-0000-000000000001");
    public static readonly Guid WeightAttributeId = new("50000000-0000-0000-0000-000000000002");
    public static readonly Guid WarrantyMonthsAttributeId = new("50000000-0000-0000-0000-000000000003");
    public static readonly Guid WaterproofAttributeId = new("50000000-0000-0000-0000-000000000004");

    public Type DbContextType => typeof(CatalogDbContext);

    private Guid Id(Guid seedId) =>
        TenantSeedIds.For(tenantContext.TenantId ?? throw new TenantNotResolvedException("catalog seeding"), seedId);

    public async Task SeedAllAsync()
    {
        await SeedCategoriesAsync();
        await SeedTagsAsync();
        await SeedAttributeTypesAsync();
    }

    private async Task SeedCategoriesAsync()
    {
        if (await context.Categories.IgnoreNonIsolationFilters().AnyAsync()) return;

        context.Categories.AddRange(
            Category.Create(LocalizedText.Create("Electronics", "إلكترونيات"), "electronics", null, 0, Id(ElectronicsId)),
            Category.Create(LocalizedText.Create("Phones", "هواتف"), "phones", Id(ElectronicsId), 0, Id(PhonesId)),
            Category.Create(LocalizedText.Create("Laptops", "حواسيب محمولة"), "laptops", Id(ElectronicsId), 1, Id(LaptopsId)),
            Category.Create(LocalizedText.Create("Home", "المنزل"), "home", null, 1, Id(HomeId)),
            Category.Create(LocalizedText.Create("Kitchen", "المطبخ"), "kitchen", Id(HomeId), 0, Id(KitchenId)),
            Category.Create(LocalizedText.Create("Services", "خدمات"), "services", null, 2, Id(ServicesId)));

        await context.SaveChangesAsync();

        if (categoryCache is not null)
            await categoryCache.InvalidateAsync();
    }

    private async Task SeedTagsAsync()
    {
        if (await context.Tags.IgnoreNonIsolationFilters().AnyAsync()) return;

        context.Tags.AddRange(
            Tag.Create(LocalizedText.Create("New Arrival", "وصل حديثاً"), Id(NewArrivalTagId)),
            Tag.Create(LocalizedText.Create("Best Seller", "الأكثر مبيعاً"), Id(BestSellerTagId)),
            Tag.Create(LocalizedText.Create("Eco Friendly", "صديق للبيئة"), Id(EcoFriendlyTagId)),
            Tag.Create(LocalizedText.Create("Limited Edition", "إصدار محدود"), Id(LimitedEditionTagId)));

        await context.SaveChangesAsync();
    }

    private async Task SeedAttributeTypesAsync()
    {
        if (await context.AttributeTypes.IgnoreNonIsolationFilters().AnyAsync()) return;

        ProductType[] physical = [ProductType.Physical];

        context.AttributeTypes.AddRange(
            AttributeType.Create(LocalizedText.Create("Color", "اللون"), AttributeDataType.Text, null, physical,
                Id(ColorAttributeId)),
            AttributeType.Create(LocalizedText.Create("Weight", "الوزن"), AttributeDataType.Number, "kg", physical,
                Id(WeightAttributeId)),
            AttributeType.Create(LocalizedText.Create("Warranty", "الضمان"), AttributeDataType.Number, "months", null,
                Id(WarrantyMonthsAttributeId)),
            AttributeType.Create(LocalizedText.Create("Waterproof", "مقاوم للماء"), AttributeDataType.Boolean, null,
                physical, Id(WaterproofAttributeId)));

        await context.SaveChangesAsync();
    }
}
