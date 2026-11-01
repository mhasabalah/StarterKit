using Catalog.Contracts.Products.Commands;
using Catalog.Domains.Products.Services;

namespace Catalog.Domains.Products.Features.UpdateProduct;

public class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(Product.NameMaxLength);

        RuleFor(x => x.Description)
            .MaximumLength(Product.DescriptionMaxLength)
            .When(x => x.Description is not null);

        RuleFor(x => x.Sku)
            .NotEmpty()
            .MaximumLength(Product.SkuMaxLength)
            .Matches(ProductValidationRules.SkuPattern)
            .WithMessage(ProductValidationRules.SkuPatternMessage);

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(Product.MaxPrice)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);

        RuleFor(x => x.ImageAttachmentId)
            .NotEqual(Guid.Empty)
            .When(x => x.ImageAttachmentId.HasValue);

        RuleFor(x => x.Version)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Version.HasValue);
    }
}

public class UpdateProductHandler(
    IProductRepository repository,
    IProductImageValidator imageValidator)
    : ICommandHandler<UpdateProductCommand, UpdateProductResult>
{
    public async Task<UpdateProductResult> Handle(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.Id, cancellationToken)
                      ?? throw new ProductNotFoundException(command.Id);

        // Optimistic concurrency. A stale client version fails fast with 409 here.
        // A concurrent write between this load and SaveChanges is still caught: the
        // AuditableEntityInterceptor pins OriginalValue to the loaded Version, so EF's
        // UPDATE ... WHERE version = @loaded throws DbUpdateConcurrencyException, which
        // ConcurrencyHandlingBehavior maps to 409.
        // (Setting Entry(..).Property(Version).OriginalValue here would be overwritten by
        // that interceptor, so the explicit comparison is what enforces the client token.)
        if (command.Version.HasValue && command.Version.Value != product.Version)
        {
            throw new ConcurrencyConflictException();
        }

        var sku = Product.NormalizeSku(command.Sku);
        if (sku != product.Sku &&
            await repository.SkuExistsAsync(sku, product.Id, cancellationToken))
        {
            throw new ProductSkuAlreadyExistsException(sku);
        }

        if (command.ImageAttachmentId is { } imageId && imageId != product.ImageAttachmentId)
        {
            await imageValidator.EnsureValidImageAsync(imageId, cancellationToken);
        }

        product.Update(
            command.Name,
            command.Description,
            command.Sku,
            command.Price,
            command.IsActive,
            command.ImageAttachmentId);

        await repository.UpdateAsync(product, cancellationToken);

        return new UpdateProductResult(true);
    }
}
