namespace Catalog.Domains.Products.Features.RestoreProduct;

public class RestoreProductCommandValidator : AbstractValidator<RestoreProductCommand>
{
    public RestoreProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
    }
}

/// <summary>Admin: undo a soft delete (the endpoint is AdminOnly).</summary>
public class RestoreProductHandler(IProductRepository repository)
    : ICommandHandler<RestoreProductCommand, RestoreProductResult>
{
    public async Task<RestoreProductResult> Handle(RestoreProductCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdIncludingDeletedAsync(command.Id, cancellationToken)
                      ?? throw new ProductNotFoundException(command.Id);

        product.Restore();
        await repository.SaveChangesAsync(cancellationToken);

        return new RestoreProductResult(true);
    }
}
