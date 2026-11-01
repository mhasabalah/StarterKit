namespace Catalog.Domains.Products.Exceptions;

public class ProductNotFoundException(Guid id)
    : NotFoundException("Product", id);

public class InvalidProductException(string message)
    : BadRequestException(message);

public class ProductSkuAlreadyExistsException(string sku)
    : ConflictException($"A product with SKU '{sku}' already exists.");
