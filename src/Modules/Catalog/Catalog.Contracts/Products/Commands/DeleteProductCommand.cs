namespace Catalog.Contracts.Products.Commands;

public record DeleteProductCommand(Guid Id) : ICommand<DeleteProductResult>;

public record DeleteProductResult(bool Success);
