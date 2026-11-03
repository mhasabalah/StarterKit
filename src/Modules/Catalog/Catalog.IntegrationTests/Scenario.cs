using Catalog.Contracts.Products.Commands;
using Catalog.Contracts.Products.Enums;
using Shared.Contracts.Localization;

namespace Catalog.IntegrationTests;

/// <summary>Builds products through the real commands (validation + domain + persistence).</summary>
public sealed class Scenario(CatalogPostgresFixture fx)
{
    public Guid Owner { get; } = Guid.NewGuid();

    public static string UniqueSku(string prefix = "IT") => $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant()[..30];

    public async Task<Guid> Create(
        string nameEn = "Widget",
        string nameAr = "أداة",
        decimal price = 10m,
        decimal? compareAt = null,
        Currency currency = Currency.USD,
        ProductType type = ProductType.Physical,
        Guid? categoryId = null,
        List<Guid>? tags = null,
        List<ProductAttributeInput>? attributes = null,
        string? description = null,
        Guid? owner = null)
    {
        var result = await fx.SendAs(owner ?? Owner, new CreateProductCommand(
            LocalizedText.Create(nameEn, nameAr), description, UniqueSku(), type, price, compareAt, currency,
            categoryId, tags, attributes));
        return result.Id;
    }

    public async Task<Guid> Published(
        string nameEn = "Widget",
        decimal price = 10m,
        Currency currency = Currency.USD,
        ProductType type = ProductType.Physical,
        Guid? categoryId = null,
        List<Guid>? tags = null,
        List<ProductAttributeInput>? attributes = null,
        string? description = null,
        Guid? owner = null)
    {
        var id = await Create(nameEn, "أداة " + nameEn, price, null, currency, type, categoryId, tags, attributes,
            description, owner);
        await Publish(id, owner ?? Owner);
        return id;
    }

    public async Task Publish(Guid id, Guid? owner = null)
    {
        await fx.SendAs(owner ?? Owner, new SubmitProductForReviewCommand(id));
        await fx.SendAsAdmin(new ApproveProductCommand(id, "Looks good"));
        await fx.SendAsAdmin(new PublishProductCommand(id));
    }
}
