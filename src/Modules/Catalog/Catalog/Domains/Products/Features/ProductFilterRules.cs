namespace Catalog.Domains.Products.Features;

/// <summary>Validation shared by every query implementing <see cref="IProductFilterQuery"/>.</summary>
internal static class ProductFilterRules
{
    public static void AddProductFilterRules<T>(
        this AbstractValidator<T> validator, IStringLocalizer<CatalogResource> localizer)
        where T : BaseQuery, IProductFilterQuery
    {
        validator.AddPagingRules(localizer);

        validator.RuleFor(x => x.Search)
            .MaximumLength(CatalogValidationRules.SearchTermMaxLength)
            .WithMessage(_ => localizer["Validation.SearchTerm.MaxLength"]);

        validator.RuleFor(x => x.MinPrice)
            .GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Price.Min"])
            .When(x => x.MinPrice.HasValue);

        validator.RuleFor(x => x.MaxPrice)
            .GreaterThanOrEqualTo(x => x.MinPrice!.Value).WithMessage(_ => localizer["Validation.PriceRange.Invalid"])
            .When(x => x.MinPrice.HasValue && x.MaxPrice.HasValue);

        validator.RuleFor(x => x.Type).IsInEnum().WithMessage(_ => localizer["Validation.ProductType.Invalid"])
            .When(x => x.Type.HasValue);
        validator.RuleFor(x => x.Status).IsInEnum().WithMessage(_ => localizer["Validation.Status.Invalid"])
            .When(x => x.Status.HasValue);
        validator.RuleFor(x => x.Currency).IsInEnum().WithMessage(_ => localizer["Validation.Currency.Invalid"])
            .When(x => x.Currency.HasValue);
    }
}
