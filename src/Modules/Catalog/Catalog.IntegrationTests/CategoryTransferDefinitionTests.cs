using Catalog.Domains.Categories.DataTransfer;
using DataTransfer.Contracts.Definitions;
using DataTransfer.Contracts.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.IntegrationTests;

/// <summary>
/// The catalog.categories import/export definitions on real Postgres, called the way the DataTransfer job calls them
/// (one fresh scope = one unit of work per batch, no HTTP user).
/// </summary>
[Collection(CatalogPostgresCollection.Name)]
public sealed class CategoryTransferDefinitionTests(CatalogPostgresFixture fx)
{
    [Fact]
    public async Task Import_then_export_round_trips_and_a_reimport_of_the_export_is_unchanged()
    {
        var p = $"rt{Guid.NewGuid():N}"[..10];
        var records = new[]
        {
            Row(2, $"{p}-root", "Root", "الجذر", order: "1"),
            Row(3, $"{p}-kid", "Kid", "الطفل", parent: $"{p}-root", order: "2"),
            Row(4, $"{p}-hidden", "Hidden", "مخفي", parent: $"{p}-root", active: "false")
        };

        var first = await ApplyAsync(false, records);

        first.Select(r => r.Outcome).Should().OnlyContain(o => o == ImportRowOutcome.Created);
        var rows = await db(d => d.Categories.AsNoTracking().Where(c => c.Slug.StartsWith(p)).ToListAsync());
        rows.Should().HaveCount(3);
        rows.Single(c => c.Slug == $"{p}-kid").ParentId.Should().Be(rows.Single(c => c.Slug == $"{p}-root").Id);
        rows.Single(c => c.Slug == $"{p}-hidden").IsActive.Should().BeFalse();

        var exported = (await ExportAsync()).Where(r => r[0]!.StartsWith(p)).ToList();
        exported.Select(r => r[0]).Should().HaveCount(3);
        exported.Select(r => r[0]).ToList().IndexOf($"{p}-root")
            .Should().BeLessThan(exported.Select(r => r[0]).ToList().IndexOf($"{p}-kid"), "parents come first");
        exported.Single(r => r[0] == $"{p}-hidden")[5].Should().Be("false");

        var names = CategoryTransferColumns.All.Select(c => c.Name).ToList();
        var again = await ApplyAsync(false, exported
            .Select((cells, i) => new ImportRecord(i + 2, names.Select((n, j) => (n, cells[j])).ToDictionary(x => x.n, x => x.Item2)))
            .ToArray());

        again.Select(r => r.Outcome).Should().OnlyContain(o => o == ImportRowOutcome.Unchanged);
    }

    [Fact]
    public async Task Updates_change_the_row_in_place_and_raise_the_cache_invalidating_event()
    {
        var slug = $"up{Guid.NewGuid():N}"[..10];
        await ApplyAsync(false, Row(2, slug, "Before", "قبل"));
        var before = await db(d => d.Categories.AsNoTracking().SingleAsync(c => c.Slug == slug));

        var result = await ApplyAsync(false, Row(2, slug.ToUpperInvariant(), "After", "بعد", order: "9"));

        result.Single().Outcome.Should().Be(ImportRowOutcome.Updated);
        var after = await db(d => d.Categories.AsNoTracking().SingleAsync(c => c.Slug == slug));
        after.Id.Should().Be(before.Id);
        after.Name.En.Should().Be("After");
        after.DisplayOrder.Should().Be(9);
        after.Version.Should().Be(before.Version + 1);
    }

    [Fact]
    public async Task A_dry_run_writes_nothing()
    {
        var slug = $"dr{Guid.NewGuid():N}"[..10];

        var result = await ApplyAsync(true, Row(2, slug, "Dry", "جاف"));

        result.Single().Outcome.Should().Be(ImportRowOutcome.Created);
        (await db(d => d.Categories.AnyAsync(c => c.Slug == slug))).Should().BeFalse();
    }

    [Fact]
    public async Task A_cycle_against_stored_categories_is_refused_and_nothing_changes()
    {
        var p = $"cy{Guid.NewGuid():N}"[..10];
        await ApplyAsync(false, Row(2, $"{p}-a", "A", "أ"), Row(3, $"{p}-b", "B", "ب", parent: $"{p}-a"));

        var result = await ApplyAsync(false, Row(2, $"{p}-a", "A", "أ", parent: $"{p}-b"));

        result.Single().Errors.Single().Code.Should().Be("parent_cycle");
        (await db(d => d.Categories.AsNoTracking().SingleAsync(c => c.Slug == $"{p}-a"))).ParentId.Should().BeNull();
    }

    private Task<T> db<T>(Func<Catalog.Data.CatalogDbContext, Task<T>> work) => fx.WithDb(work);

    private async Task<IReadOnlyList<ImportRowResult>> ApplyAsync(bool dryRun, params ImportRecord[] records)
    {
        fx.Http.SetUser(null, false); // background job: no HTTP user
        IReadOnlyList<ImportRowResult> results = [];
        await fx.WithScope(async sp =>
        {
            var definition = sp.GetServices<IImportDefinition>().Single(d => d.Key == CategoryTransferColumns.Key);
            results = await definition.ApplyBatchAsync(records,
                new ImportContext(Guid.NewGuid(), Guid.NewGuid(), dryRun, "en"), CancellationToken.None);
        });
        return results;
    }

    private async Task<List<IReadOnlyList<string?>>> ExportAsync()
    {
        var rows = new List<IReadOnlyList<string?>>();
        await fx.WithScope(async sp =>
        {
            var export = sp.GetServices<IExportDefinition>().Single(d => d.Key == CategoryTransferColumns.Key);
            await foreach (var row in export.StreamAsync(
                               new ExportContext(Guid.NewGuid(), Guid.NewGuid(), new Dictionary<string, string>(), "en"),
                               CancellationToken.None))
                rows.Add(row);
        });
        return rows;
    }

    private static ImportRecord Row(long row, string slug, string nameEn, string nameAr, string? parent = null,
        string? order = null, string? active = null) =>
        new(row, new Dictionary<string, string?>
        {
            [CategoryTransferColumns.Slug] = slug,
            [CategoryTransferColumns.NameEn] = nameEn,
            [CategoryTransferColumns.NameAr] = nameAr,
            [CategoryTransferColumns.Parent] = parent,
            [CategoryTransferColumns.DisplayOrder] = order,
            [CategoryTransferColumns.Active] = active
        });
}
