namespace Catalog.Contracts.Products.Commands;

/// <param name="Version">
/// Optional optimistic-concurrency token from <c>ProductDto.Version</c>.
/// When supplied and stale, the update is rejected with HTTP 409.
/// </param>
public record UpdateProductCommand(
    Guid Id,
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    bool IsActive,
    Guid? ImageAttachmentId = null,
    long? Version = null)
    : ICommand<UpdateProductResult>;

public record UpdateProductResult(bool Success);
