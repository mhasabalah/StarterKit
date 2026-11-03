namespace Catalog.Domains.Products.Features.UpdateProduct;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        Include(new ProductDetailsValidator(localizer));
        RuleFor(x => x.Version).ValidVersion(localizer).When(x => x.Version.HasValue);
    }
}

/// <summary>
/// Full replace of the editable fields. Tags/attributes are applied as a delta (only real changes hit the
/// database). Concurrency: <c>dbContext.ExpectVersion</c> makes SaveChanges run
/// <c>UPDATE ... WHERE version = @clientVersion</c>; a stale version fails with 409.
/// </summary>
public class UpdateProductHandler(
    IProductRepository repository,
    CatalogDbContext dbContext,
    IProductAuthorizationService authorization,
    IProductLookupValidator lookupValidator)
    : ICommandHandler<UpdateProductCommand, UpdateProductResult>
{
    public async Task<UpdateProductResult> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.Id, cancellationToken)
                      ?? throw new ProductNotFoundException(command.Id);

        authorization.EnsureCanEdit(product);

        dbContext.ExpectVersion(product, command.Version);

        var sku = Product.NormalizeSku(command.Sku);
        if (sku != product.Sku && await repository.SkuExistsAsync(sku, product.Id, cancellationToken))
            throw new ProductSkuAlreadyExistsException(sku);

        var tagIds = command.TagIds ?? [];
        var attributes = command.Attributes ?? [];
        await lookupValidator.EnsureValidAsync(command.Type, command.CategoryId, tagIds, attributes, cancellationToken);

        product.Update(
            command.Name,
            command.Description,
            command.Sku,
            command.Type,
            Pricing.Create(command.Price, command.CompareAtPrice, command.Currency),
            command.CategoryId);

        product.SyncTags(tagIds);
        product.SyncAttributes(attributes.ToDictionary(a => a.AttributeTypeId, a => a.Value));

        await repository.SaveChangesAsync(cancellationToken);

        return new UpdateProductResult(true, product.Version);
    }
}
