namespace Catalog.Domains.Products.Features.DeleteProduct;

public class DeleteProductCommandValidator : AbstractValidator<DeleteProductCommand>
{
    public DeleteProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
    }
}

/// <summary>Soft delete (owner or admin). The row stays; the query filter hides it until restore/purge.</summary>
public class DeleteProductHandler(IProductRepository repository, IProductAuthorizationService authorization)
    : ICommandHandler<DeleteProductCommand, DeleteProductResult>
{
    public async Task<DeleteProductResult> Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.Id, cancellationToken)
                      ?? throw new ProductNotFoundException(command.Id);

        authorization.EnsureCanModify(product);

        product.Delete();
        await repository.SaveChangesAsync(cancellationToken);

        return new DeleteProductResult(true);
    }
}
