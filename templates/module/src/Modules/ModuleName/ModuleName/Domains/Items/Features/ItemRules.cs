using System.Globalization;
using System.Linq.Expressions;
using ModuleName.Domains.Items.Models;

namespace ModuleName.Domains.Items.Features;

/// <summary>Validation and mapping shared by the Item features (DRY).</summary>
internal static class ItemRules
{
    public const int MaxPageSize = 100;
    public const int SearchMaxLength = 100;

    public static void AddNameRules<T>(this AbstractValidator<T> validator, Expression<Func<T, string>> name,
        IStringLocalizer<ModuleNameResource> localizer) =>
        validator.RuleFor(name)
            .NotEmpty().WithMessage(_ => localizer["Validation.Name.Required"])
            .MaximumLength(Item.NameMaxLength)
            .WithMessage(_ => Format(localizer["Validation.Name.MaxLength"], Item.NameMaxLength));

    public static void AddDescriptionRules<T>(this AbstractValidator<T> validator,
        Expression<Func<T, string?>> description, IStringLocalizer<ModuleNameResource> localizer) =>
        validator.RuleFor(description)
            .MaximumLength(Item.DescriptionMaxLength)
            .WithMessage(_ => Format(localizer["Validation.Description.MaxLength"], Item.DescriptionMaxLength));

    /// <summary>The read model, projected in SQL by the query handlers.</summary>
    public static readonly Expression<Func<Item, ItemDto>> Projection = item =>
        new ItemDto(item.Id, item.Code, item.Name, item.Description, item.CreatedAt, item.Version);

    private static string Format(LocalizedString template, int maxLength) =>
        template.Value.Replace("{MaxLength}", maxLength.ToString(CultureInfo.InvariantCulture));
}
