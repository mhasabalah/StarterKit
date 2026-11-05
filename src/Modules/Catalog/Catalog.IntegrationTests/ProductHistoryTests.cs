using Catalog.Contracts.Products.Commands;
using Catalog.Contracts.Products.Enums;
using Catalog.Contracts.Products.Queries;
using Catalog.Domains.Products.Exceptions;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Localization;
using Shared.Core.Auditing;
using Shared.Core.MultiTenancy;

namespace Catalog.IntegrationTests;

/// <summary>Audit trail of products on real PostgreSQL: same transaction, diffs, history query, tenant isolation.</summary>
[Collection(CatalogPostgresCollection.Name)]
public sealed class ProductHistoryTests(CatalogPostgresFixture fx)
{
    private readonly Scenario _s = new(fx);

    private Task<Shared.Contracts.Pagination.PaginatedResult<Contracts.Products.Dtos.ProductHistoryEntryDto>> History(Guid id) =>
        fx.SendAsAdmin(new GetProductHistoryQuery(id));

    [Fact]
    public async Task CreateAndUpdate_AreRecorded_WithActor_AndOldNewValues_NewestFirst()
    {
        var id = await _s.Create("Original", price: 10m);
        await fx.SendAs(_s.Owner, new UpdateProductCommand(id, LocalizedText.Create("Renamed", "اسم"), "d", Scenario.UniqueSku(),
            ProductType.Physical, 25m, null, Currency.USD));

        var page = await History(id);

        page.PageInfoResult.TotalItems.Should().Be(2);
        var update = page.Result![0];
        update.Action.Should().Be("Modified");
        update.ActorUserId.Should().Be(_s.Owner);
        update.Changes.GetProperty("Name.En").GetProperty("old").GetString().Should().Be("Original");
        update.Changes.GetProperty("Name.En").GetProperty("new").GetString().Should().Be("Renamed");
        update.Changes.GetProperty("Pricing.Price").GetProperty("new").GetDecimal().Should().Be(25m);
        update.Changes.TryGetProperty("Version", out _).Should().BeFalse();
        update.Changes.TryGetProperty("UpdatedAt", out _).Should().BeFalse();

        var created = page.Result![1];
        created.Action.Should().Be("Added");
        created.Changes.GetProperty("Sku").GetProperty("new").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UnknownProduct_IsNotFound()
    {
        await fx.Awaiting(f => f.SendAsAdmin(new GetProductHistoryQuery(Guid.NewGuid())))
            .Should().ThrowAsync<ProductNotFoundException>();
    }

    [Fact]
    public async Task AnotherTenant_CannotSeeTheHistory_NorAnyAuditRow()
    {
        var id = await _s.Create("Tenant-bound");
        var other = new TenantInfo(Guid.NewGuid(), "other-tenant", "Other");

        using (TenantScope.Begin(other))
        {
            await fx.Awaiting(f => f.SendAsAdmin(new GetProductHistoryQuery(id)))
                .Should().ThrowAsync<ProductNotFoundException>();
            var visible = await fx.WithDb(db => db.AuditEntries.AsNoTracking()
                .CountAsync(a => a.EntityId == id.ToString()));
            visible.Should().Be(0);
        }

        (await History(id)).PageInfoResult.TotalItems.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task FailedSave_LeavesNoAuditRowBehind()
    {
        var id = Guid.NewGuid();
        var before = await fx.WithDb(db => db.AuditEntries.CountAsync());

        // Duplicate SKU in one unit of work: the product insert fails, so its audit row must not exist.
        var sku = Scenario.UniqueSku();
        await fx.SendAs(_s.Owner, new CreateProductCommand(LocalizedText.Create("A", "ا"), null, sku, ProductType.Physical,
            1m, null, Currency.USD, null, null, null));
        var afterFirst = await fx.WithDb(db => db.AuditEntries.CountAsync());
        await fx.Awaiting(f => f.SendAs(_s.Owner, new CreateProductCommand(LocalizedText.Create("B", "ب"), null, sku,
                ProductType.Physical, 1m, null, Currency.USD, null, null, null)))
            .Should().ThrowAsync<Exception>();

        afterFirst.Should().Be(before + 1);
        (await fx.WithDb(db => db.AuditEntries.CountAsync())).Should().Be(afterFirst);
        id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AuditRows_AreTenantOwned_AndPurgedByRetention()
    {
        var id = await _s.Create("Old");
        await fx.WithDb(async db =>
        {
            await db.AuditEntries.Where(a => a.EntityId == id.ToString())
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.OccurredAt, DateTime.UtcNow.AddDays(-400)));
            return 0;
        });

        var removed = await fx.WithDb(db => db.PurgeAuditEntriesAsync(DateTime.UtcNow.AddDays(-365), 1000));

        removed.Should().BeGreaterThanOrEqualTo(1);
        (await fx.WithDb(db => db.AuditEntries.CountAsync(a => a.EntityId == id.ToString()))).Should().Be(0);
    }
}
