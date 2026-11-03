namespace Catalog.Domains.Categories.Features;

internal static class CategoryRules
{
    public static void AddCategoryFieldRules<T>(
        this AbstractValidator<T> validator,
        IStringLocalizer<CatalogResource> localizer,
        System.Linq.Expressions.Expression<Func<T, LocalizedText>> name,
        System.Linq.Expressions.Expression<Func<T, string>> slug,
        System.Linq.Expressions.Expression<Func<T, Guid?>> parentId,
        System.Linq.Expressions.Expression<Func<T, int>> displayOrder)
    {
        validator.RuleFor(name).ValidLocalizedName(localizer, Category.NameMaxLength);

        validator.RuleFor(slug)
            .NotEmpty().WithMessage(_ => localizer["Validation.Slug.Required"])
            .MaximumLength(Category.SlugMaxLength).WithMessage(_ => localizer["Validation.Slug.MaxLength"])
            .Must(Category.IsValidSlug).WithMessage(_ => localizer["Validation.Slug.Pattern"]);

        validator.RuleFor(parentId)
            .NotEqual(Guid.Empty).WithMessage(_ => localizer["Validation.Id.Required"]);

        validator.RuleFor(displayOrder)
            .GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.DisplayOrder.Min"]);
    }
}
