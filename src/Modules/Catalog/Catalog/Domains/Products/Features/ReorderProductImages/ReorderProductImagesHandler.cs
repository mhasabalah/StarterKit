namespace Catalog.Domains.Products.Features.ReorderProductImages;

public class ReorderProductImagesCommandValidator : AbstractValidator<ReorderProductImagesCommand>
{
    public ReorderProductImagesCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.OrderedImageIds)
            .NotEmpty().WithMessage(_ => localizer["Validation.ImageIds.Required"])
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage(_ => localizer["Validation.ImageIds.Unique"])
            .When(x => x.OrderedImageIds is not null);
        RuleFor(x => x.OrderedImageIds).NotNull().WithMessage(_ => localizer["Validation.ImageIds.Required"]);
    }
}

/// <summary>The aggregate checks the list is a permutation of its gallery and renumbers 0..n-1.</summary>
public class ReorderProductImagesHandler(IProductRepository repository, IProductAuthorizationService authorization)
    : ICommandHandler<ReorderProductImagesCommand, ReorderProductImagesResult>
{
    public async Task<ReorderProductImagesResult> Handle(
        ReorderProductImagesCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.ProductId, cancellationToken)
                      ?? throw new ProductNotFoundException(command.ProductId);

        authorization.EnsureCanEdit(product);

        product.ReorderImages(command.OrderedImageIds);
        await repository.SaveChangesAsync(cancellationToken);

        return new ReorderProductImagesResult(true);
    }
}
