namespace Shared.Core.Localization.Services;

/// <summary>
/// Resolves the culture-aware display name for any enum value decorated with
/// <see cref="Attributes.LocalizedDescriptionAttribute"/>.
/// </summary>
public interface IEnumLocalizationService
{
    /// <summary>
    /// Returns the localized display name of <paramref name="value"/>.
    /// Arabic is returned when the current UI culture is "ar"; English otherwise.
    /// Falls back to <c>value.ToString()</c> when no attribute is present.
    /// </summary>
    string Localize<TEnum>(TEnum value) where TEnum : Enum;
}
