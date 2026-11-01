using Catalog.Contracts.Products.Commands;

namespace Catalog.Domains.Products.Features.DeleteProduct;

public class DeleteProductCommandValidator : AbstractValidator<DeleteProductCommand>
{
    public DeleteProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}

public class DeleteProductHandler(IProductRepository repository)
    : ICommandHandler<DeleteProductCommand, DeleteProductResult>
{
    public async Task<DeleteProductResult> Handle(
        DeleteProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.Id, cancellationToken)
                      ?? throw new ProductNotFoundException(command.Id);

        product.MarkDeleted();
        await repository.DeleteAsync(product, cancellationToken);

        return new DeleteProductResult(true);
    }
}
