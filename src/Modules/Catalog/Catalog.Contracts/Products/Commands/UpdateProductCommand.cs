namespace Catalog.Contracts.Products.Commands;

/// <param name="Version">
/// Optional optimistic-concurrency token from the read DTO (<c>version</c>).
/// When supplied and stale, the update is rejected with HTTP 409.
/// </param>
public record UpdateProductCommand(
    Guid Id,
    LocalizedText Name,
    string? Description,
    string Sku,
    ProductType Type,
    decimal Price,
    decimal? CompareAtPrice,
    Currency Currency,
    Guid? CategoryId = null,
    List<Guid>? TagIds = null,
    List<ProductAttributeInput>? Attributes = null,
    long? Version = null)
    : ICommand<UpdateProductResult>, IProductDetails;

public record UpdateProductResult(bool Success, long Version);
