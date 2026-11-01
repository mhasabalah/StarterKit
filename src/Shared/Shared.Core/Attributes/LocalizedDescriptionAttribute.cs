namespace Shared.Core.Attributes;

/// <summary>
/// Provides bilingual (English/Arabic) localized descriptions for enum values.
/// Apply this attribute to enum fields to enable automatic translation in the EnumLookupService.
/// </summary>
/// <example>
/// <code>
/// public enum OrderStatus
/// {
///     [LocalizedDescription("Pending", "قيد الانتظار")]
///     Pending = 0,
///
///     [LocalizedDescription("Completed", "مكتمل")]
///     Completed = 1
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public class LocalizedDescriptionAttribute : Attribute
{
    /// <summary>
    /// Gets the English description of the enum value.
    /// </summary>
    public string DescriptionEn { get; }

    /// <summary>
    /// Gets the Arabic description of the enum value.
    /// </summary>
    public string DescriptionAr { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizedDescriptionAttribute"/> class.
    /// </summary>
    /// <param name="descriptionEn">The English description.</param>
    /// <param name="descriptionAr">The Arabic description.</param>
    public LocalizedDescriptionAttribute(string descriptionEn, string descriptionAr)
    {
        DescriptionEn = descriptionEn;
        DescriptionAr = descriptionAr;
    }
}
