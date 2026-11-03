namespace Catalog.Domains.Products.Features;

/// <summary>
/// Rules shared by <see cref="CreateProductCommand"/> and <see cref="UpdateProductCommand"/> (both implement
/// <see cref="IProductDetails"/>); each command validator <c>Include</c>s it. Messages come from
/// CatalogResource (en/ar). The aggregate re-checks its invariants (defence in depth).
/// </summary>
public class ProductDetailsValidator : AbstractValidator<IProductDetails>
{
    public const string SkuPattern = "^[A-Za-z0-9][A-Za-z0-9._-]*$";

    public ProductDetailsValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Name).ValidLocalizedName(localizer, Product.NameMaxLength);

        RuleFor(x => x.Description)
            .MaximumLength(Product.DescriptionMaxLength).WithMessage(_ => localizer["Validation.Description.MaxLength"])
            .When(x => x.Description is not null);

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage(_ => localizer["Validation.Sku.Required"])
            .MaximumLength(Product.SkuMaxLength).WithMessage(_ => localizer["Validation.Sku.MaxLength"])
            .Matches(SkuPattern).WithMessage(_ => localizer["Validation.Sku.Pattern"]);

        RuleFor(x => x.Type).IsInEnum().WithMessage(_ => localizer["Validation.ProductType.Invalid"]);
        RuleFor(x => x.Currency).IsInEnum().WithMessage(_ => localizer["Validation.Currency.Invalid"]);

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Price.Min"])
            .LessThanOrEqualTo(Pricing.MaxAmount).WithMessage(_ => localizer["Validation.Price.Max"])
            .PrecisionScale(18, 2, ignoreTrailingZeros: true).WithMessage(_ => localizer["Validation.Price.Precision"]);

        RuleFor(x => x.CompareAtPrice)
            .GreaterThan(x => x.Price).WithMessage(_ => localizer["Validation.CompareAtPrice.GreaterThanPrice"])
            .LessThanOrEqualTo(Pricing.MaxAmount).WithMessage(_ => localizer["Validation.Price.Max"])
            .When(x => x.CompareAtPrice.HasValue);

        RuleFor(x => x.CategoryId)
            .NotEqual(Guid.Empty).WithMessage(_ => localizer["Validation.Id.Required"])
            .When(x => x.CategoryId.HasValue);

        RuleFor(x => x.TagIds)
            .Must(ids => ids!.Distinct().Count() == ids!.Count).WithMessage(_ => localizer["Validation.TagIds.Unique"])
            .Must(ids => ids!.All(id => id != Guid.Empty)).WithMessage(_ => localizer["Validation.TagIds.NotEmpty"])
            .When(x => x.TagIds is not null);

        RuleFor(x => x.Attributes)
            .Must(a => a!.Select(x => x.AttributeTypeId).Distinct().Count() == a!.Count)
            .WithMessage(_ => localizer["Validation.Attributes.UniqueType"])
            .When(x => x.Attributes is not null);

        RuleForEach(x => x.Attributes).ChildRules(attribute =>
        {
            attribute.RuleFor(a => a.AttributeTypeId)
                .NotEmpty().WithMessage(_ => localizer["Validation.AttributeTypeId.Required"]);
            attribute.RuleFor(a => a.Value)
                .NotEmpty().WithMessage(_ => localizer["Validation.AttributeValue.Required"])
                .MaximumLength(ProductAttribute.ValueMaxLength)
                .WithMessage(_ => localizer["Validation.AttributeValue.MaxLength"]);
        });
    }
}
