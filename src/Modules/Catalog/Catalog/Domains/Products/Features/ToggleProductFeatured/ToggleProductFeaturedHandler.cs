namespace Catalog.Domains.Products.Features.ToggleProductFeatured;

public class ToggleProductFeaturedCommandValidator : AbstractValidator<ToggleProductFeaturedCommand>
{
    public ToggleProductFeaturedCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
    }
}

/// <summary>Admin status toggle — non-critical, so no version token (see CLAUDE.md concurrency rules).</summary>
public class ToggleProductFeaturedHandler(IProductRepository repository)
    : ICommandHandler<ToggleProductFeaturedCommand>
{
    public async Task<Unit> Handle(ToggleProductFeaturedCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.ProductId, cancellationToken)
                      ?? throw new ProductNotFoundException(command.ProductId);

        product.SetFeatured(command.IsFeatured);
        await repository.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
