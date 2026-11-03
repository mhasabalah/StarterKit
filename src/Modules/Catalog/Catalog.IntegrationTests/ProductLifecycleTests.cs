using Catalog.Contracts.Products.Commands;
using Catalog.Contracts.Products.Enums;
using Catalog.Contracts.Products.IntegrationEvents;
using Catalog.Contracts.Products.Queries;
using Catalog.Contracts.Wishlist;
using Catalog.Data;
using Catalog.Data.Repository;
using Catalog.Data.Seed;
using Catalog.Domains.Products.Exceptions;
using Catalog.Domains.Products.Jobs;
using Catalog.Domains.Products.Models;
using Catalog.Domains.Wishlist.Models;
using FluentValidation;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Localization;
using Shared.Core.Exceptions;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;

namespace Catalog.IntegrationTests;

[Collection(CatalogPostgresCollection.Name)]
public sealed class ProductLifecycleTests(CatalogPostgresFixture fx)
{
    private readonly Scenario _s = new(fx);

    private Task<long> VersionOf(Guid id) =>
        fx.WithDb(db => db.Products.IgnoreQueryFilters().Where(p => p.Id == id).Select(p => p.Version).SingleAsync());

    private Task<List<TenantOutboxMessage>> OutboxRowsFor(Guid productId) =>
        fx.WithDb(async db => (await db.Set<TenantOutboxMessage>().AsNoTracking().ToListAsync())
            .Where(m => m.Payload.Contains(productId.ToString())).ToList());

    private UpdateProductCommand Update(Guid id, long? version, string nameEn = "Renamed", List<Guid>? tags = null) =>
        new(id, LocalizedText.Create(nameEn, "معدل"), null, Scenario.UniqueSku(), ProductType.Physical, 20m, null,
            Currency.USD, null, tags ?? [], [], version);

    // ── Optimistic concurrency through the real AuditableEntityInterceptor ────────────────

    [Fact]
    public async Task Update_WithCurrentVersion_Succeeds_AndBumpsVersionByOne()
    {
        var id = await _s.Create();
        var version = await VersionOf(id);

        var result = await fx.SendAs(_s.Owner, Update(id, version));

        result.Version.Should().Be(version + 1);
        (await VersionOf(id)).Should().Be(version + 1);
    }

    [Fact]
    public async Task Update_WithStaleVersion_IsRejected_AndNothingChanges()
    {
        var id = await _s.Create("Original");
        var stale = await VersionOf(id);
        await fx.SendAs(_s.Owner, Update(id, stale, "First writer"));

        var act = () => fx.SendAs(_s.Owner, Update(id, stale, "Lost update"));

        (await act.Should().ThrowAsync<ConcurrencyConflictException>())
            .WithInnerException<DbUpdateConcurrencyException>();
        (await fx.WithDb(db => db.Products.Where(p => p.Id == id).Select(p => p.Name.En).SingleAsync()))
            .Should().Be("First writer");
    }

    [Fact]
    public async Task ChildOnlyChange_StillBumpsTheAggregateVersion()
    {
        var id = await _s.Create(tags: [CatalogDataSeeder.NewArrivalTagId]);
        var version = await VersionOf(id);

        await fx.WithScope(async sp =>
        {
            fx.Http.SetUser(_s.Owner, false);
            var repository = sp.GetRequiredService<IProductRepository>();
            var product = (await repository.GetByIdAsync(id))!;
            product.AddTag(CatalogDataSeeder.BestSellerTagId);
            await repository.SaveChangesAsync();
        });

        (await VersionOf(id)).Should().Be(version + 1);
    }

    [Fact]
    public async Task WorkflowCommand_WithStaleVersion_IsRejected()
    {
        var id = await _s.Create();
        var stale = await VersionOf(id);
        await fx.SendAs(_s.Owner, Update(id, stale));

        var act = () => fx.SendAs(_s.Owner, new SubmitProductForReviewCommand(id, stale));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ── Workflow + integration event ────────────────────────────────────────────────

    [Fact]
    public async Task FullWorkflow_PublishesTheIntegrationEvent_AfterCommit()
    {
        var id = await _s.Create();

        var submitted = await fx.SendAs(_s.Owner, new SubmitProductForReviewCommand(id));
        var approved = await fx.SendAsAdmin(new ApproveProductCommand(id, "ok", submitted.Version));
        (await OutboxRowsFor(id)).Should().BeEmpty("only publishing raises the integration event");
        var published = await fx.SendAsAdmin(new PublishProductCommand(id, approved.Version));

        published.Status.Should().Be(ProductStatus.Published);
        var rows = await OutboxRowsFor(id);
        var row = rows.Should().ContainSingle().Subject;
        row.MessageType.Should().Be("urn:message:catalog.product-published.v1");
        row.TenantId.Should().Be(TenantDefaults.DefaultTenantId);
        row.Payload.Should().Contain(id.ToString()).And.Contain(_s.Owner.ToString());

        var archived = await fx.SendAs(_s.Owner, new ArchiveProductCommand(id, published.Version));
        archived.Status.Should().Be(ProductStatus.Archived);
        (await fx.SendAs(_s.Owner, new UnarchiveProductCommand(id))).Status.Should().Be(ProductStatus.Draft);
    }

    [Fact]
    public async Task InvalidCommand_IsStoppedByTheValidationPipeline()
    {
        var act = () => fx.SendAs(_s.Owner, new CreateProductCommand(
            LocalizedText.Create("", ""), null, "bad sku", ProductType.Physical, -1m, null, Currency.USD));

        var error = await act.Should().ThrowAsync<ValidationException>();
        error.Which.Errors.Select(e => e.PropertyName).Should().Contain(["Name.En", "Sku", "Price"]);
    }

    [Fact]
    public async Task Create_WithInactiveOrInapplicableLookups_IsRejected()
    {
        var act = () => _s.Create(type: ProductType.Service, attributes:
            [new ProductAttributeInput(CatalogDataSeeder.ColorAttributeId, "Red")]); // Color is Physical-only

        await act.Should().ThrowAsync<InvalidProductException>().WithMessage("*does not apply*");

        var badNumber = () => _s.Create(attributes:
            [new ProductAttributeInput(CatalogDataSeeder.WeightAttributeId, "heavy")]);
        await badNumber.Should().ThrowAsync<InvalidProductException>().WithMessage("*not a valid Number*");

        var unknownTag = () => _s.Create(tags: [Guid.NewGuid()]);
        await unknownTag.Should().ThrowAsync<InvalidProductException>();
    }

    [Fact]
    public async Task DuplicateSku_IsRejected_EvenAgainstSoftDeletedProducts()
    {
        var id = await _s.Create();
        var sku = await fx.WithDb(db => db.Products.Where(p => p.Id == id).Select(p => p.Sku).SingleAsync());
        await fx.SendAs(_s.Owner, new DeleteProductCommand(id));

        var act = () => fx.SendAs(_s.Owner, new CreateProductCommand(
            LocalizedText.Create("x", "س"), null, sku.ToLowerInvariant(), ProductType.Physical, 1m, null, Currency.USD));

        await act.Should().ThrowAsync<ProductSkuAlreadyExistsException>();
    }

    [Fact]
    public async Task ConcurrentDuplicateSku_IsTranslatedFrom23505To409()
    {
        var sku = Scenario.UniqueSku("RACE");
        Product Make() => Product.Create(LocalizedText.Create("r", "ر"), null, sku, ProductType.Physical,
            Pricing.Create(1m, null, Currency.USD), _s.Owner);

        await fx.WithScope(sp => sp.GetRequiredService<IProductRepository>().AddAsync(Make()));
        var second = () => fx.WithScope(sp => sp.GetRequiredService<IProductRepository>().AddAsync(Make()));

        await second.Should().ThrowAsync<ProductSkuAlreadyExistsException>();
    }

    // ── Soft delete / restore / purge ─────────────────────────────────────────────────

    [Fact]
    public async Task SoftDelete_HidesEverywhere_ExceptAdminIncludeDeleted_AndRestoreBringsItBack()
    {
        var id = await _s.Published("Deletable");
        await fx.SendAs(_s.Owner, new DeleteProductCommand(id));

        (await fx.SendAs(null, new GetProductsQuery { OwnerId = _s.Owner })).Result.Should().BeEmpty();
        await FluentActions.Invoking(() => fx.SendAs(null, new GetProductByIdQuery(id)))
            .Should().ThrowAsync<ProductNotFoundException>();
        (await fx.SendAsAdmin(new GetAdminProductsQuery { OwnerId = _s.Owner })).Result.Should().BeEmpty();
        var withDeleted = await fx.SendAsAdmin(new GetAdminProductsQuery { OwnerId = _s.Owner, IncludeDeleted = true });
        withDeleted.Result!.Single().IsDeleted.Should().BeTrue();
        (await fx.WithDb(db => db.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == id)))
            .Should().BeTrue("soft delete keeps the row");

        await fx.SendAsAdmin(new RestoreProductCommand(id));

        (await fx.SendAs(null, new GetProductByIdQuery(id))).Product.Id.Should().Be(id);
    }

    [Fact]
    public async Task PurgeJob_HardDeletesOnlyExpiredSoftDeletes_AndCascadesChildren()
    {
        var old = await _s.Published("Old", tags: [CatalogDataSeeder.EcoFriendlyTagId]);
        var recent = await _s.Create("Recent");
        var visitor = Guid.NewGuid();
        await fx.SendAs(visitor, new AddToWishlistCommand(old));
        await fx.SendAs(_s.Owner, new DeleteProductCommand(old));
        await fx.SendAs(_s.Owner, new DeleteProductCommand(recent));
        await fx.WithDb(db => db.Products.IgnoreQueryFilters().Where(p => p.Id == old)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTime.UtcNow.AddDays(-31))));

        var purged = 0;
        await fx.WithScope(async sp =>
            purged = await sp.GetRequiredService<PurgeSoftDeletedProductsJob>().ExecuteAsync(JobCancellationToken.Null));

        purged.Should().BeGreaterThanOrEqualTo(1);
        (await fx.WithDb(db => db.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == old))).Should().BeFalse();
        (await fx.WithDb(db => db.ProductTags.IgnoreQueryFilters().AnyAsync(t => t.ProductId == old))).Should().BeFalse();
        (await fx.WithDb(db => db.WishlistItems.IgnoreQueryFilters().AnyAsync(w => w.ProductId == old))).Should().BeFalse();
        (await fx.WithDb(db => db.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == recent)))
            .Should().BeTrue("inside the retention window");
    }

    // ── Gallery persistence (child entities added to a loaded aggregate are INSERTed) ─────

    [Fact]
    public async Task Gallery_AddReorderSetPrimaryRemove_RoundTripsThroughPostgres()
    {
        var id = await _s.Create();
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await Mutate(id, p =>
        {
            p.AddImage(a, "https://cdn/a.jpg");
            p.AddImage(b, "https://cdn/b.jpg");
            p.AddImage(c, "https://cdn/c.jpg");
        });
        var imageIds = await fx.WithDb(db => db.ProductImages.Where(i => i.ProductId == id)
            .OrderBy(i => i.DisplayOrder).Select(i => i.Id).ToListAsync());
        imageIds.Should().HaveCount(3);

        await Mutate(id, p => p.ReorderImages([imageIds[2], imageIds[0], imageIds[1]]));
        await Mutate(id, p => p.SetPrimaryImage(imageIds[1]));
        await Mutate(id, p => p.RemoveImage(imageIds[0]));

        var detail = (await fx.SendAs(_s.Owner, new GetProductByIdQuery(id))).Product;
        detail.Images.Select(i => i.AttachmentId).Should().Equal(c, b);
        detail.Images.Select(i => i.DisplayOrder).Should().Equal(0, 1);
        detail.PrimaryImage!.AttachmentId.Should().Be(b);
    }

    private Task Mutate(Guid id, Action<Product> change) => fx.WithScope(async sp =>
    {
        var repository = sp.GetRequiredService<IProductRepository>();
        var product = (await repository.GetByIdAsync(id))!;
        change(product);
        await repository.SaveChangesAsync();
    });

    // ── Wishlist ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Wishlist_AddFlagDuplicateAndRemove()
    {
        var id = await _s.Published("Wish");
        var shopper = Guid.NewGuid();

        await fx.SendAs(shopper, new AddToWishlistCommand(id));

        (await fx.SendAs(shopper, new GetProductsQuery { OwnerId = _s.Owner })).Result!.Single().IsWishlisted.Should().BeTrue();
        (await fx.SendAs(Guid.NewGuid(), new GetProductsQuery { OwnerId = _s.Owner })).Result!.Single().IsWishlisted
            .Should().BeFalse();
        (await fx.SendAs(null, new GetProductsQuery { OwnerId = _s.Owner })).Result!.Single().IsWishlisted
            .Should().BeFalse();

        var duplicate = () => fx.SendAs(shopper, new AddToWishlistCommand(id));
        await duplicate.Should().ThrowAsync<ConflictException>();

        var list = await fx.SendAs(shopper, new GetWishlistQuery());
        list.Result!.Single().Should().Match<WishlistItemDto>(w => w.ProductId == id && w.IsAvailable);

        await fx.SendAs(shopper, new RemoveFromWishlistCommand(id));
        (await fx.SendAs(shopper, new GetWishlistQuery())).Result.Should().BeEmpty();
    }

    [Fact]
    public async Task Wishlist_UniqueIndex_IsTheRealGuard()
    {
        var id = await _s.Published("Race");
        var shopper = Guid.NewGuid();

        await fx.WithScope(sp => sp.GetRequiredService<IWishlistRepository>().AddAsync(WishlistItem.Create(shopper, id)));
        var second = () => fx.WithScope(sp =>
            sp.GetRequiredService<IWishlistRepository>().AddAsync(WishlistItem.Create(shopper, id)));

        await second.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Wishlist_FollowsTheProductsSoftDelete()
    {
        var id = await _s.Published("Fragile");
        var shopper = Guid.NewGuid();
        await fx.SendAs(shopper, new AddToWishlistCommand(id));

        await fx.SendAs(_s.Owner, new DeleteProductCommand(id));
        (await fx.SendAs(shopper, new GetWishlistQuery())).Result.Should().BeEmpty();

        await fx.SendAsAdmin(new RestoreProductCommand(id));
        (await fx.SendAs(shopper, new GetWishlistQuery())).Result.Should().ContainSingle();
    }

    [Fact]
    public async Task Wishlist_RejectsUnpublishedProducts()
    {
        var draft = await _s.Create();

        var act = () => fx.SendAs(Guid.NewGuid(), new AddToWishlistCommand(draft));

        await act.Should().ThrowAsync<ProductNotFoundException>();
    }

    [Fact]
    public async Task Wishlist_ShowsArchivedProductsAsUnavailable()
    {
        var id = await _s.Published("Gone soon");
        var shopper = Guid.NewGuid();
        await fx.SendAs(shopper, new AddToWishlistCommand(id));

        await fx.SendAs(_s.Owner, new ArchiveProductCommand(id));

        (await fx.SendAs(shopper, new GetWishlistQuery())).Result!.Single().IsAvailable.Should().BeFalse();
    }
}
