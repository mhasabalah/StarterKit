using Catalog.Contracts.Products.Enums;
using Catalog.Domains.Products.DataTransfer;
using Catalog.Domains.Products.Models;
using DataTransfer.Contracts.Definitions;
using DataTransfer.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Localization;

namespace Catalog.IntegrationTests;

/// <summary>
/// The catalog.products import/export definitions on real Postgres, called the way the DataTransfer job calls them
/// (one fresh scope = one unit of work per batch, no HTTP user).
/// </summary>
[Collection(CatalogPostgresCollection.Name)]
public sealed class ProductTransferDefinitionTests(CatalogPostgresFixture fx)
{
    private readonly Scenario _scenario = new(fx);

    [Fact]
    public async Task New_skus_become_draft_products_owned_by_the_requester()
    {
        var owner = Guid.NewGuid();
        var sku = Scenario.UniqueSku("IMP");
        var category = await ActiveCategorySlugAsync();

        var results = await ApplyAsync(owner, dryRun: false,
            Row(2, sku, price: "19.50", category: category),
            Row(3, Scenario.UniqueSku("IMP"), type: "digital", currency: "usd"));

        results.Select(r => r.Outcome).Should().Equal(ImportRowOutcome.Created, ImportRowOutcome.Created);
        var product = await fx.WithDb(db => db.Products.AsNoTracking().Include(p => p.Category)
            .SingleAsync(p => p.Sku == sku));
        product.Should().BeEquivalentTo(new { Status = ProductStatus.Draft, OwnerId = owner, Type = ProductType.Physical });
        product.Pricing.Price.Should().Be(19.50m);
        product.Category!.Slug.Should().Be(category);
    }

    [Fact]
    public async Task A_dry_run_reports_outcomes_but_writes_nothing()
    {
        var sku = Scenario.UniqueSku("DRY");

        var results = await ApplyAsync(Guid.NewGuid(), dryRun: true, Row(2, sku));

        results.Single().Outcome.Should().Be(ImportRowOutcome.Created);
        (await fx.WithDb(db => db.Products.AnyAsync(p => p.Sku == sku))).Should().BeFalse();
    }

    [Fact]
    public async Task An_existing_sku_of_the_requester_is_updated_and_identical_values_are_unchanged()
    {
        var id = await _scenario.Create(nameEn: "Old", price: 5m);
        var sku = await fx.WithDb(db => db.Products.Where(p => p.Id == id).Select(p => p.Sku).SingleAsync());
        var version = await VersionAsync(id);

        var updated = await ApplyAsync(_scenario.Owner, false, Row(2, sku.ToLowerInvariant(), nameEn: "New", price: "7.00"));
        var again = await ApplyAsync(_scenario.Owner, false, Row(2, sku, nameEn: "New", price: "7.00"));

        updated.Single().Outcome.Should().Be(ImportRowOutcome.Updated);
        again.Single().Outcome.Should().Be(ImportRowOutcome.Unchanged);
        var product = await fx.WithDb(db => db.Products.AsNoTracking().SingleAsync(p => p.Id == id));
        product.Name.En.Should().Be("New");
        product.Version.Should().Be(version + 1, "the unchanged re-import did not touch the row");
    }

    [Fact]
    public async Task Someone_elses_product_or_a_published_one_is_not_editable_and_is_left_alone()
    {
        var foreign = await _scenario.Create(nameEn: "Theirs");
        var published = await _scenario.Published(nameEn: "Live");
        var skus = await fx.WithDb(db => db.Products.Where(p => p.Id == foreign || p.Id == published)
            .ToDictionaryAsync(p => p.Id, p => p.Sku));

        var byStranger = await ApplyAsync(Guid.NewGuid(), false, Row(2, skus[foreign], nameEn: "Hijacked"));
        var byOwnerWhileLive = await ApplyAsync(_scenario.Owner, false, Row(2, skus[published], nameEn: "Changed"));

        byStranger.Single().Errors.Single().Code.Should().Be("not_editable");
        byOwnerWhileLive.Single().Errors.Single().Code.Should().Be("not_editable");
        (await fx.WithDb(db => db.Products.AsNoTracking().SingleAsync(p => p.Id == foreign))).Name.En.Should().Be("Theirs");
    }

    [Fact]
    public async Task Invalid_cells_fail_their_row_with_the_column_and_a_localized_message_and_valid_rows_still_save()
    {
        var good = Scenario.UniqueSku("OK");

        var results = await ApplyAsync(Guid.NewGuid(), false,
            Row(2, good),
            Row(3, Scenario.UniqueSku("BAD"), price: "abc"),
            Row(4, Scenario.UniqueSku("BAD"), currency: "XYZ"),
            Row(5, Scenario.UniqueSku("BAD"), category: "no-such-category"),
            Row(6, "bad sku!"),
            Row(7, Scenario.UniqueSku("BAD"), price: "10", compareAt: "5"),
            Row(8, Scenario.UniqueSku("BAD"), nameAr: null),
            Row(9, Scenario.UniqueSku("AR"), price: "x", culture: "ar"));

        results[0].Outcome.Should().Be(ImportRowOutcome.Created);
        results.Skip(1).Should().OnlyContain(r => r.Outcome == ImportRowOutcome.Failed);
        results.Skip(1).Select(r => (r.Errors[0].Column, r.Errors[0].Code)).Should().Equal(
            ("price", "invalid_number"), ("currency", "invalid_value"), ("category", "category_not_found"),
            ("sku", "invalid"), ("compare_at_price", "invalid"), ("name_ar", "required"), ("price", "invalid_number"));
        results[^1].Errors[0].Message.Should().Contain("ليس رقمًا");
        (await fx.WithDb(db => db.Products.AnyAsync(p => p.Sku == good))).Should().BeTrue();
    }

    [Fact]
    public async Task A_sku_repeated_in_the_batch_updates_the_row_created_earlier_in_it()
    {
        var sku = Scenario.UniqueSku("DUP");
        var owner = Guid.NewGuid();

        var results = await ApplyAsync(owner, false, Row(2, sku, nameEn: "First"), Row(3, sku, nameEn: "Second"));

        results.Select(r => r.Outcome).Should().Equal(ImportRowOutcome.Created, ImportRowOutcome.Updated);
        (await fx.WithDb(db => db.Products.AsNoTracking().SingleAsync(p => p.Sku == sku))).Name.En.Should().Be("Second");
    }

    [Fact]
    public async Task Export_streams_products_in_import_column_order_and_reimports_unchanged()
    {
        var owner = Guid.NewGuid();
        var id = await _scenario.Create(nameEn: "=Formula", price: 12.3m, owner: owner);

        var rows = new List<IReadOnlyList<string?>>();
        await fx.WithScope(async sp =>
        {
            var export = sp.GetServices<IExportDefinition>().Single(d => d.Key == ProductTransferColumns.Key);
            var context = new ExportContext(Guid.NewGuid(), owner, new Dictionary<string, string> { ["mine"] = "true" }, "en");
            await foreach (var row in export.StreamAsync(context, CancellationToken.None))
                rows.Add(row);
        });

        rows.Should().ContainSingle();
        var cells = rows[0];
        cells[1].Should().Be("=Formula");
        cells[5].Should().Be("12.30");
        cells[^1].Should().Be(nameof(ProductStatus.Draft));

        var columns = ProductTransferColumns.Export.Select(c => c.Name).ToList();
        var record = new ImportRecord(2, columns.Select((c, i) => (c, cells[i])).ToDictionary(x => x.c, x => x.Item2));
        var reimport = await ApplyAsync(owner, false, record);
        reimport.Single().Outcome.Should().Be(ImportRowOutcome.Unchanged);
        (await VersionAsync(id)).Should().Be(0);
    }

    private async Task<IReadOnlyList<ImportRowResult>> ApplyAsync(Guid requestedBy, bool dryRun, params ImportRecord[] records)
    {
        fx.Http.SetUser(null, false); // background job: no HTTP user
        IReadOnlyList<ImportRowResult> results = [];
        var culture = records.Select(r => r.Get("__culture")).FirstOrDefault(c => c is not null) ?? "en";
        // Like the DataTransfer job: the UI culture is the requester's for the whole call.
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            await fx.WithScope(async sp =>
            {
                var definition = sp.GetServices<IImportDefinition>().Single(d => d.Key == ProductTransferColumns.Key);
                results = await definition.ApplyBatchAsync(records,
                    new ImportContext(Guid.NewGuid(), requestedBy, dryRun, culture), CancellationToken.None);
            });
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }

        return results;
    }

    private static ImportRecord Row(long row, string sku, string? nameEn = "Imported", string? nameAr = "مستورد",
        string? price = "10.00", string type = "Physical", string currency = "EGP", string? category = null,
        string? compareAt = null, string? culture = null) =>
        new(row, new Dictionary<string, string?>
        {
            [ProductTransferColumns.Sku] = sku,
            [ProductTransferColumns.NameEn] = nameEn,
            [ProductTransferColumns.NameAr] = nameAr,
            [ProductTransferColumns.Type] = type,
            [ProductTransferColumns.Price] = price,
            [ProductTransferColumns.CompareAtPrice] = compareAt,
            [ProductTransferColumns.Currency] = currency,
            [ProductTransferColumns.Category] = category,
            ["__culture"] = culture
        });

    private Task<long> VersionAsync(Guid id) =>
        fx.WithDb(db => db.Products.Where(p => p.Id == id).Select(p => p.Version).SingleAsync());

    private Task<string> ActiveCategorySlugAsync() =>
        fx.WithDb(db => db.Categories.Where(c => c.IsActive).Select(c => c.Slug).FirstAsync());
}
