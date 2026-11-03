using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Catalog.Domains.Products.Features.CreateProduct;
using Catalog.Domains.Products.Features.DeleteProduct;
using Catalog.Domains.Products.Features.PurgeProduct;
using Catalog.Domains.Products.Features.RestoreProduct;
using Catalog.Domains.Products.Features.UpdateProduct;
using Catalog.Domains.Products.Features.UploadProductImage;
using Microsoft.Extensions.Logging.Abstractions;
using static Catalog.Tests.TestSupport;

namespace Catalog.Tests.Features;

public class CreateProductHandlerTests
{
    private readonly Mock<IProductRepository> _repository = new();
    private readonly Mock<IProductLookupValidator> _lookups = new();
    private readonly Mock<IProductImageValidator> _images = new();
    private readonly Mock<ISender> _sender = new();

    private CreateProductHandler Handler(Guid? userId, bool admin = false) => new(
        _repository.Object, AuthAs(userId, admin), _lookups.Object, _images.Object, _sender.Object,
        NullLogger<CreateProductHandler>.Instance);

    private static CreateProductCommand Command(Guid? ownerId = null, Guid? session = null) =>
        new(Name(), "desc", " wid-001 ", ProductType.Physical, 9.99m, 12m, Currency.EUR,
            TagIds: [Guid.NewGuid()], Attributes: [new ProductAttributeInput(Guid.NewGuid(), "Red")],
            AttachmentSessionId: session, OwnerId: ownerId);

    [Fact]
    public async Task ValidCommand_CreatesDraftOwnedByCaller_WithTagsAndAttributes()
    {
        Product? added = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback<Product, CancellationToken>((p, _) => added = p);

        var result = await Handler(OwnerId).Handle(Command(), CancellationToken.None);

        added.Should().NotBeNull();
        result.Should().Be(new CreateProductResult(added!.Id, ImagesAssociated: true));
        added.OwnerId.Should().Be(OwnerId);
        added.Status.Should().Be(ProductStatus.Draft);
        added.Sku.Should().Be("WID-001");
        added.Pricing.CompareAtPrice.Should().Be(12m);
        added.Tags.Should().ContainSingle();
        added.Attributes.Should().ContainSingle().Which.Value.Should().Be("Red");
        _repository.Verify(r => r.SkuExistsAsync("WID-001", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NonAdmin_CannotCreateForSomeoneElse()
    {
        Product? added = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback<Product, CancellationToken>((p, _) => added = p);

        await Handler(OwnerId).Handle(Command(ownerId: StrangerId), CancellationToken.None);

        added!.OwnerId.Should().Be(OwnerId);
    }

    [Fact]
    public async Task Admin_CanCreateForASeller()
    {
        Product? added = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback<Product, CancellationToken>((p, _) => added = p);

        await Handler(AdminId, admin: true).Handle(Command(ownerId: OwnerId), CancellationToken.None);

        added!.OwnerId.Should().Be(OwnerId);
    }

    [Fact]
    public async Task DuplicateSku_Throws409_AndDoesNotAdd()
    {
        _repository.Setup(r => r.SkuExistsAsync("WID-001", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => Handler(OwnerId).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<ProductSkuAlreadyExistsException>().WithMessage("*WID-001*");
        _repository.Verify(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvalidLookups_Propagate_AndDoNotAdd()
    {
        _lookups.Setup(l => l.EnsureValidAsync(It.IsAny<ProductType>(), It.IsAny<Guid?>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<ProductAttributeInput>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidProductException("inactive tag"));

        var act = () => Handler(OwnerId).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProductException>();
        _repository.Verify(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithSession_AddsOnlyImageAttachmentsToTheGallery()
    {
        var session = Guid.NewGuid();
        var image = Guid.NewGuid();
        var pdf = Guid.NewGuid();
        Product? added = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback<Product, CancellationToken>((p, _) => added = p);
        _sender.Setup(s => s.Send(It.IsAny<AssociatePendingAttachmentsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssociatePendingAttachmentsResult(2, [pdf, image],
            [
                new AssociatedAttachmentMetadata(pdf, "https://x/a.pdf", 0, false),
                new AssociatedAttachmentMetadata(image, "https://x/b.jpg", 1, false)
            ]));
        _images.Setup(v => v.IsImageAsync(image, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _images.Setup(v => v.IsImageAsync(pdf, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Handler(OwnerId).Handle(Command(session: session), CancellationToken.None);

        result.ImagesAssociated.Should().BeTrue();
        added!.Images.Should().ContainSingle().Which.AttachmentId.Should().Be(image);
        added.PrimaryImage!.AttachmentId.Should().Be(image);
        _sender.Verify(s => s.Send(It.Is<AssociatePendingAttachmentsCommand>(c =>
            c.SessionId == session && c.EntityType == EntityType.Product && c.EntityId == added.Id &&
            c.UserId == OwnerId), It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenAssociationFails_TheProductIsStillCreated()
    {
        _sender.Setup(s => s.Send(It.IsAny<AssociatePendingAttachmentsCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));

        var result = await Handler(OwnerId).Handle(Command(session: Guid.NewGuid()), CancellationToken.None);

        result.ImagesAssociated.Should().BeFalse();
        _repository.Verify(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenSessionIsEmpty_ReportsNotAssociated()
    {
        _sender.Setup(s => s.Send(It.IsAny<AssociatePendingAttachmentsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssociatePendingAttachmentsResult(0, [], []));

        var result = await Handler(OwnerId).Handle(Command(session: Guid.NewGuid()), CancellationToken.None);

        result.ImagesAssociated.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidPriceBypassingValidator_DomainGuardThrows()
    {
        var act = () => Handler(OwnerId).Handle(Command() with { Price = -5m, CompareAtPrice = null },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProductException>();
    }
}

public class UpdateProductHandlerTests
{
    private readonly Mock<IProductRepository> _repository = new();
    private readonly Mock<IProductLookupValidator> _lookups = new();
    private readonly CatalogDbContext _db = DisconnectedDbContext();
    private readonly Product _product;

    public UpdateProductHandlerTests()
    {
        _product = NewProduct(OwnerId);
        _product.AddTag(Guid.NewGuid());
        _product.Version = 3; // as loaded after three prior writes
        _db.Attach(_product);
        _repository.Setup(r => r.GetByIdAsync(_product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_product);
    }

    private UpdateProductHandler Handler(Guid userId, bool admin = false) =>
        new(_repository.Object, _db, AuthAs(userId, admin), _lookups.Object);

    private UpdateProductCommand Command(long? version = null, string sku = "WID-001", List<Guid>? tags = null) =>
        new(_product.Id, Name("Widget v2", "أداة 2"), "new", sku, ProductType.Physical, 12.5m, null, Currency.USD,
            null, tags ?? [], [], version);

    private long OriginalVersion => _db.Entry(_product).Property(p => p.Version).OriginalValue;

    [Fact]
    public async Task WithVersion_CallsExpectVersion_SoEfChecksTheClientsToken()
    {
        await Handler(OwnerId).Handle(Command(version: 2), CancellationToken.None);

        OriginalVersion.Should().Be(2, "ExpectVersion puts the client's version into OriginalValue → UPDATE ... WHERE version = 2");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithoutVersion_KeepsTheLoadedVersion()
    {
        await Handler(OwnerId).Handle(Command(), CancellationToken.None);

        OriginalVersion.Should().Be(3);
    }

    [Fact]
    public async Task UpdatesDetails_AndSyncsTags()
    {
        var newTag = Guid.NewGuid();

        var result = await Handler(OwnerId).Handle(Command(tags: [newTag]), CancellationToken.None);

        result.Success.Should().BeTrue();
        _product.Name.En.Should().Be("Widget v2");
        _product.Pricing.Price.Should().Be(12.5m);
        _product.Tags.Select(t => t.TagId).Should().Equal(newTag);
    }

    [Fact]
    public async Task Missing_ThrowsNotFound()
    {
        var act = () => Handler(OwnerId).Handle(Command() with { Id = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<ProductNotFoundException>();
    }

    [Fact]
    public async Task NonOwner_IsForbidden_AndNothingIsSaved()
    {
        var act = () => Handler(StrangerId).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _product.Name.En.Should().Be("Widget");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OwnerOfAPendingProduct_IsForbidden_ButAdminIsNot()
    {
        _product.SubmitForReview();

        await FluentActions.Invoking(() => Handler(OwnerId).Handle(Command(), CancellationToken.None))
            .Should().ThrowAsync<ForbiddenException>();
        await FluentActions.Invoking(() => Handler(AdminId, admin: true).Handle(Command(), CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task UnchangedSkuModuloCase_SkipsUniquenessCheck()
    {
        await Handler(OwnerId).Handle(Command(sku: " wid-001 "), CancellationToken.None);

        _repository.Verify(r => r.SkuExistsAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ChangedSkuToTakenValue_Throws409()
    {
        _repository.Setup(r => r.SkuExistsAsync("TAKEN", _product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var act = () => Handler(OwnerId).Handle(Command(sku: "taken"), CancellationToken.None);

        await act.Should().ThrowAsync<ProductSkuAlreadyExistsException>();
        _product.Sku.Should().Be("WID-001");
    }
}

public class DeleteRestorePurgeHandlerTests
{
    private readonly Mock<IProductRepository> _repository = new();

    [Fact]
    public async Task Delete_SoftDeletes_ForTheOwner()
    {
        var product = NewProduct(OwnerId);
        _repository.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(product);

        var result = await new DeleteProductHandler(_repository.Object, AuthAs(OwnerId))
            .Handle(new DeleteProductCommand(product.Id), CancellationToken.None);

        result.Success.Should().BeTrue();
        product.IsDeleted.Should().BeTrue();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.HardDeleteAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_ByStranger_IsForbidden()
    {
        var product = NewProduct(OwnerId);
        _repository.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(product);

        var act = () => new DeleteProductHandler(_repository.Object, AuthAs(StrangerId))
            .Handle(new DeleteProductCommand(product.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        product.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Restore_UsesTheIncludingDeletedLookup()
    {
        var product = NewProduct();
        product.Delete();
        _repository.Setup(r => r.GetByIdIncludingDeletedAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        await new RestoreProductHandler(_repository.Object)
            .Handle(new RestoreProductCommand(product.Id), CancellationToken.None);

        product.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Purge_HardDeletesOnlySoftDeletedProducts()
    {
        var live = NewProduct();
        var deleted = NewProduct(sku: "DEL-1");
        deleted.Delete();
        _repository.Setup(r => r.GetByIdIncludingDeletedAsync(live.Id, It.IsAny<CancellationToken>())).ReturnsAsync(live);
        _repository.Setup(r => r.GetByIdIncludingDeletedAsync(deleted.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleted);
        var handler = new PurgeProductHandler(_repository.Object, NullLogger<PurgeProductHandler>.Instance);

        (await handler.Handle(new PurgeProductCommand(live.Id), CancellationToken.None)).Purged.Should().BeFalse();
        (await handler.Handle(new PurgeProductCommand(deleted.Id), CancellationToken.None)).Purged.Should().BeTrue();

        _repository.Verify(r => r.HardDeleteAsync(deleted, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.HardDeleteAsync(live, It.IsAny<CancellationToken>()), Times.Never);
        deleted.DomainEvents.OfType<ProductPurgedEvent>().Should().ContainSingle();
    }
}

public class UploadProductImageHandlerTests
{
    private readonly Mock<IProductRepository> _repository = new();
    private readonly Mock<ISender> _sender = new();
    private readonly Product _product = NewProduct(OwnerId);
    private readonly Guid _attachmentId = Guid.NewGuid();

    public UploadProductImageHandlerTests()
    {
        _repository.Setup(r => r.GetByIdAsync(_product.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_product);
        _sender.Setup(s => s.Send(It.IsAny<UploadAttachmentCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UploadAttachmentResult(_attachmentId, "f.png", "https://cdn/f.png", null, 10, "image/png"));
    }

    private UploadProductImageHandler Handler(Guid userId) =>
        new(_repository.Object, AuthAs(userId), _sender.Object, NullLogger<UploadProductImageHandler>.Instance);

    private UploadProductImageCommand Command() =>
        new(_product.Id, "f.png", "image/png", 10, Stream.Null, "front");

    [Fact]
    public async Task UploadsAsProductImage_AndAddsToTheGallery()
    {
        var result = await Handler(OwnerId).Handle(Command(), CancellationToken.None);

        result.AttachmentId.Should().Be(_attachmentId);
        result.IsPrimary.Should().BeTrue();
        _product.Images.Should().ContainSingle().Which.Url.Should().Be("https://cdn/f.png");
        _sender.Verify(s => s.Send(It.Is<UploadAttachmentCommand>(c =>
            c.Type == AttachmentType.Image && c.EntityType == EntityType.Product && c.EntityId == _product.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenSavingFails_TheUploadedAttachmentIsDeleted()
    {
        _repository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var act = () => Handler(OwnerId).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _sender.Verify(s => s.Send(It.Is<DeleteAttachmentCommand>(c => c.Id == _attachmentId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FullGallery_IsRejectedBeforeUploading()
    {
        for (var i = 0; i < Product.MaxImages; i++)
            _product.AddImage(Guid.NewGuid(), $"https://cdn/{i}.png");

        var act = () => Handler(OwnerId).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProductException>();
        _sender.Verify(s => s.Send(It.IsAny<UploadAttachmentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stranger_IsForbidden_BeforeUploading()
    {
        var act = () => Handler(StrangerId).Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _sender.Verify(s => s.Send(It.IsAny<UploadAttachmentCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
