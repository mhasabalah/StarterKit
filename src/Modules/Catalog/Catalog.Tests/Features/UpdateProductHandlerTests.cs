using Catalog.Domains.Products.Features.UpdateProduct;
using FluentValidation.TestHelper;
using Shared.Core.Exceptions;

namespace Catalog.Tests.Features;

public class UpdateProductHandlerTests
{
    private readonly Mock<IProductRepository> _repository = new();
    private readonly Mock<IProductImageValidator> _imageValidator = new();
    private readonly UpdateProductHandler _handler;
    private readonly Product _existing;

    public UpdateProductHandlerTests()
    {
        _handler = new UpdateProductHandler(_repository.Object, _imageValidator.Object);

        _existing = Product.Create("Widget", "desc", "WID-001", 10m, true, Guid.NewGuid());
        _existing.ClearDomainEvents();
        _existing.Version = 3; // as loaded from the DB after three prior writes

        _repository.Setup(r => r.GetByIdAsync(_existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_existing);
    }

    private UpdateProductCommand Command(
        string sku = "WID-001", long? version = null, Guid? imageId = null, bool keepImage = true) =>
        new(_existing.Id, "Widget v2", "new desc", sku, 12.5m, false,
            keepImage ? imageId ?? _existing.ImageAttachmentId : imageId, version);

    [Fact]
    public async Task Handle_ProductMissing_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();

        var act = () => _handler.Handle(
            new UpdateProductCommand(missingId, "n", null, "S", 1m, true), CancellationToken.None);

        await act.Should().ThrowAsync<ProductNotFoundException>();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WithoutVersion_UpdatesProduct()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Success.Should().BeTrue();
        _existing.Name.Should().Be("Widget v2");
        _existing.Price.Should().Be(12.5m);
        _existing.IsActive.Should().BeFalse();
        _existing.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductUpdatedEvent>();
        _repository.Verify(r => r.UpdateAsync(_existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithMatchingVersion_UpdatesProduct()
    {
        await _handler.Handle(Command(version: 3), CancellationToken.None);

        _repository.Verify(r => r.UpdateAsync(_existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task Handle_WithStaleVersion_ThrowsConcurrencyConflictAndDoesNotModify(long version)
    {
        var act = () => _handler.Handle(Command(version: version), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        _existing.Name.Should().Be("Widget");
        _existing.DomainEvents.Should().BeEmpty();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SkuUnchangedModuloCase_SkipsUniquenessCheck()
    {
        await _handler.Handle(Command(sku: " wid-001 "), CancellationToken.None);

        _repository.Verify(r => r.SkuExistsAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_SkuChangedToFreeValue_ChecksExcludingSelfAndUpdates()
    {
        await _handler.Handle(Command(sku: "new-sku"), CancellationToken.None);

        _repository.Verify(r => r.SkuExistsAsync("NEW-SKU", _existing.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _existing.Sku.Should().Be("NEW-SKU");
    }

    [Fact]
    public async Task Handle_SkuChangedToTakenValue_ThrowsConflict()
    {
        _repository.Setup(r => r.SkuExistsAsync("TAKEN", _existing.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var act = () => _handler.Handle(Command(sku: "taken"), CancellationToken.None);

        await act.Should().ThrowAsync<ProductSkuAlreadyExistsException>();
        _existing.Sku.Should().Be("WID-001");
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ImageUnchanged_DoesNotRevalidate()
    {
        await _handler.Handle(Command(), CancellationToken.None);

        _imageValidator.Verify(
            v => v.EnsureValidImageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NewImage_IsValidated()
    {
        var newImage = Guid.NewGuid();

        await _handler.Handle(Command(imageId: newImage), CancellationToken.None);

        _imageValidator.Verify(v => v.EnsureValidImageAsync(newImage, It.IsAny<CancellationToken>()),
            Times.Once);
        _existing.ImageAttachmentId.Should().Be(newImage);
    }

    [Fact]
    public async Task Handle_ImageRemoved_ClearsWithoutValidation()
    {
        await _handler.Handle(Command(imageId: null, keepImage: false), CancellationToken.None);

        _existing.ImageAttachmentId.Should().BeNull();
        _imageValidator.Verify(
            v => v.EnsureValidImageAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_InvalidNewImage_PropagatesAndDoesNotSave()
    {
        var newImage = Guid.NewGuid();
        _imageValidator.Setup(v => v.EnsureValidImageAsync(newImage, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidProductException("not an image"));

        var act = () => _handler.Handle(Command(imageId: newImage), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidProductException>();
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Validator_RejectsNegativeVersionAndPrice()
    {
        var validator = new UpdateProductCommandValidator();
        var command = new UpdateProductCommand(Guid.NewGuid(), "n", null, "S", -1m, true, null, -1);

        var result = validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.Price);
        result.ShouldHaveValidationErrorFor(x => x.Version);
    }

    [Fact]
    public void Validator_RejectsEmptyId()
    {
        var validator = new UpdateProductCommandValidator();

        validator.TestValidate(new UpdateProductCommand(Guid.Empty, "n", null, "S", 1m, true))
            .ShouldHaveValidationErrorFor(x => x.Id);
    }
}
