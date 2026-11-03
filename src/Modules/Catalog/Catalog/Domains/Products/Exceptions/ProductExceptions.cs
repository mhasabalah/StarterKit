namespace Catalog.Domains.Products.Exceptions;

public class ProductNotFoundException(Guid id)
    : NotFoundException("Product", id);

public class ProductImageNotFoundException(Guid productId, Guid imageId)
    : NotFoundException($"Image '{imageId}' was not found on product '{productId}'.");

public class InvalidProductException(string message)
    : BadRequestException(message);

public class ProductSkuAlreadyExistsException(string sku)
    : ConflictException($"A product with SKU '{sku}' already exists.");

/// <summary>The requested workflow step is not allowed from the product's current status (HTTP 400).</summary>
public class InvalidProductStatusTransitionException(ProductStatus from, ProductStatus to)
    : BadRequestException($"A product cannot move from '{from}' to '{to}'.")
{
    public ProductStatus From { get; } = from;
    public ProductStatus To { get; } = to;
}
