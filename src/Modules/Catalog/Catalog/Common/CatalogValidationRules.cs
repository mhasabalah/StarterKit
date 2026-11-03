namespace Catalog.Common;

/// <summary>Validation building blocks shared by several Catalog validators (DRY).</summary>
internal static class CatalogValidationRules
{
    public const int MaxPageSize = 100;
    public const int SearchTermMaxLength = 200;

    /// <summary>Both languages required, each within <paramref name="maxLength"/>.</summary>
    public static IRuleBuilderOptions<T, LocalizedText> ValidLocalizedName<T>(
        this IRuleBuilder<T, LocalizedText> rule,
        IStringLocalizer<CatalogResource> localizer,
        int maxLength) =>
        rule.NotNull().WithMessage(_ => localizer["Validation.Name.Required"])
            .SetValidator(new LocalizedTextValidator(localizer, maxLength));

    public static void AddPagingRules<T>(
        this AbstractValidator<T> validator, IStringLocalizer<CatalogResource> localizer)
        where T : BaseQuery
    {
        validator.RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);

        validator.RuleFor(x => x.PageSize)
            .InclusiveBetween(1, MaxPageSize).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
    }

    public static IRuleBuilderOptions<T, long?> ValidVersion<T>(
        this IRuleBuilder<T, long?> rule, IStringLocalizer<CatalogResource> localizer) =>
        rule.GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"]);
}

internal sealed class LocalizedTextValidator : AbstractValidator<LocalizedText>
{
    public LocalizedTextValidator(IStringLocalizer<CatalogResource> localizer, int maxLength)
    {
        RuleFor(x => x.En)
            .NotEmpty().WithMessage(_ => localizer["Validation.Name.En.Required"])
            .MaximumLength(maxLength).WithMessage(_ => localizer["Validation.Name.MaxLength"]);

        RuleFor(x => x.Ar)
            .NotEmpty().WithMessage(_ => localizer["Validation.Name.Ar.Required"])
            .MaximumLength(maxLength).WithMessage(_ => localizer["Validation.Name.MaxLength"]);
    }
}
