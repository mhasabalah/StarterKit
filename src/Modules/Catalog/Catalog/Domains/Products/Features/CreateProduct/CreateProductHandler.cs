using Catalog.Contracts.Products.Commands;
using Catalog.Domains.Products.Services;

namespace Catalog.Domains.Products.Features.CreateProduct;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
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
    }
}

public class CreateProductHandler(
    IProductRepository repository,
    IProductImageValidator imageValidator)
    : ICommandHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<CreateProductResult> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        var sku = Product.NormalizeSku(command.Sku);

        if (await repository.SkuExistsAsync(sku, cancellationToken: cancellationToken))
        {
            throw new ProductSkuAlreadyExistsException(sku);
        }

        if (command.ImageAttachmentId is { } imageId)
        {
            await imageValidator.EnsureValidImageAsync(imageId, cancellationToken);
        }

        var product = Product.Create(
            command.Name,
            command.Description,
            command.Sku,
            command.Price,
            command.IsActive,
            command.ImageAttachmentId);

        await repository.AddAsync(product, cancellationToken);

        return new CreateProductResult(product.Id);
    }
}
