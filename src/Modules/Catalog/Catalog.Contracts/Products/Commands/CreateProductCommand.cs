namespace Catalog.Contracts.Products.Commands;

public record CreateProductCommand(
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    bool IsActive = true,
    Guid? ImageAttachmentId = null)
    : ICommand<CreateProductResult>;

public record CreateProductResult(Guid Id);
