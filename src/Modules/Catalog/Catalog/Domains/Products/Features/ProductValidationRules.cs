namespace Catalog.Domains.Products.Features;

/// <summary>Validation constants shared by the Create and Update validators.</summary>
internal static class ProductValidationRules
{
    public const string SkuPattern = "^[A-Za-z0-9][A-Za-z0-9._-]*$";

    public const string SkuPatternMessage =
        "SKU must start with a letter or digit and contain only letters, digits, '.', '_' and '-'.";
}
