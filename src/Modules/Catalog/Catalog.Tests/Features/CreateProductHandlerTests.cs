using Catalog.Domains.Products.Features.CreateProduct;

namespace Catalog.Tests.Features;

public class CreateProductHandlerTests
{
    private readonly Mock<IProductRepository> _repository = new();
    private readonly Mock<IProductImageValidator> _imageValidator = new();
    private readonly CreateProductHandler _handler;

    public CreateProductHandlerTests()
    {
        _handler = new CreateProductHandler(_repository.Object, _imageValidator.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_AddsProductAndReturnsId()
    {
        Product? added = null;
        _repository.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .Callback<Product, CancellationToken>((p, _) => added = p)
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(
            new CreateProductCommand("Widget", "desc", "wid-001", 9.99m, IsActive: false),
            CancellationToken.None);

        added.Should().NotBeNull();
        result.Id.Should().Be(added!.Id);
        added.Name.Should().Be("Widget");
        added.Sku.Should().Be("WID-001");
        added.Price.Should().Be(9.99m);
        added.IsActive.Should().BeFalse();
        added.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductCreatedEvent>();
    }

    [Fact]
    public async Task Handle_ChecksUniquenessAgainstNormalizedSku()
    {
        await _handler.Handle(new CreateProductCommand("Widget", null, "  wid-001 ", 1m),
            CancellationToken.None);

        _repository.Verify(r => r.SkuExistsAsync("WID-001", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateSku_ThrowsConflictAndDoesNotAdd()
    {
        _repository.Setup(r => r.SkuExistsAsync("WID-001", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _handler.Handle(new CreateProductCommand("Widget", null, "wid-001", 1m),
            CancellationToken.None);

        await act.Should().ThrowAsync<ProductSkuAlreadyExistsException>().WithMessage("*WID-001*");
        _repository.Verify(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithoutImage_DoesNotCallAttachmentModule()
    {
        await _handler.Handle(new CreateProductCommand("Widget", null, "SKU1", 1m),
            CancellationToken.None);

        _imageValidator.Verify(
            v => v.EnsureValidImageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithImage_ValidatesImageThenAdds()
    {
        var imageId = Guid.NewGuid();

        await _handler.Handle(new CreateProductCommand("Widget", null, "SKU1", 1m, true, imageId),
            CancellationToken.None);

        _imageValidator.Verify(v => v.EnsureValidImageAsync(imageId, It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(r => r.AddAsync(
                It.Is<Product>(p => p.ImageAttachmentId == imageId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_InvalidImage_PropagatesAndDoesNotAdd()
    {
        var imageId = Guid.NewGuid();
        _imageValidator.Setup(v => v.EnsureValidImageAsync(imageId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidProductException("not found"));

        var act = () => _handler.Handle(
            new CreateProductCommand("Widget", null, "SKU1", 1m, true, imageId),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProductException>();
        _repository.Verify(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidPriceBypassingValidator_DomainGuardThrows()
    {
        // Defence in depth: even if the pipeline validator is skipped (e.g. a direct
        // in-process call), the aggregate refuses an invalid state.
        var act = () => _handler.Handle(new CreateProductCommand("Widget", null, "SKU1", -5m),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProductException>();
        _repository.Verify(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_RepositoryRaceOnSku_PropagatesConflict()
    {
        // Pre-check passes, but a concurrent insert wins — the repository translates
        // the unique-index violation into ProductSkuAlreadyExistsException.
        _repository.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProductSkuAlreadyExistsException("SKU1"));

        var act = () => _handler.Handle(new CreateProductCommand("Widget", null, "SKU1", 1m),
            CancellationToken.None);

        await act.Should().ThrowAsync<ProductSkuAlreadyExistsException>();
    }
}
