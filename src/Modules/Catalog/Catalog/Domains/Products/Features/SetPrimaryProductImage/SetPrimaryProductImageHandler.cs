namespace Catalog.Domains.Products.Features.SetPrimaryProductImage;

public class SetPrimaryProductImageCommandValidator : AbstractValidator<SetPrimaryProductImageCommand>
{
    public SetPrimaryProductImageCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.ImageId).NotEmpty().WithMessage(_ => localizer["Validation.ImageId.Required"]);
    }
}

public class SetPrimaryProductImageHandler(IProductRepository repository, IProductAuthorizationService authorization)
    : ICommandHandler<SetPrimaryProductImageCommand, SetPrimaryProductImageResult>
{
    public async Task<SetPrimaryProductImageResult> Handle(
        SetPrimaryProductImageCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.ProductId, cancellationToken)
                      ?? throw new ProductNotFoundException(command.ProductId);

        authorization.EnsureCanEdit(product);

        product.SetPrimaryImage(command.ImageId); // 404 when the image is not in this product's gallery
        await repository.SaveChangesAsync(cancellationToken);

        return new SetPrimaryProductImageResult(true);
    }
}
