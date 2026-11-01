using Catalog.Domains.Products.Features.DeleteProduct;

namespace Catalog.Tests.Features;

public class DeleteProductHandlerTests
{
    private readonly Mock<IProductRepository> _repository = new();

    [Fact]
    public async Task Handle_ProductMissing_ThrowsNotFound()
    {
        var handler = new DeleteProductHandler(_repository.Object);

        var act = () => handler.Handle(new DeleteProductCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<ProductNotFoundException>();
        _repository.Verify(r => r.DeleteAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ProductExists_RaisesDeletedEventAndDeletes()
    {
        var product = Product.Create("Widget", null, "SKU1", 1m);
        product.ClearDomainEvents();
        _repository.Setup(r => r.GetByIdAsync(product.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        var handler = new DeleteProductHandler(_repository.Object);

        var result = await handler.Handle(new DeleteProductCommand(product.Id), CancellationToken.None);

        result.Success.Should().BeTrue();
        product.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ProductDeletedEvent>();
        _repository.Verify(r => r.DeleteAsync(product, It.IsAny<CancellationToken>()), Times.Once);
    }
}
