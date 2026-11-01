using Shared.Contracts.Enums;

namespace Shared.Core.Enums;

/// <summary>
/// Service for generic enum lookup with bilingual localization support.
/// Discovers enums from registered assemblies and provides their values with translations.
/// </summary>
public interface IEnumLookupService
{
    /// <summary>
    /// Retrieves enum values with bilingual descriptions for the specified enum name.
    /// </summary>
    /// <param name="enumName">The enum name (case-insensitive, accepts kebab-case, snake_case, or PascalCase).</param>
    /// <returns>An <see cref="EnumResponseDto"/> containing all enum values with localized descriptions.</returns>
    /// <exception cref="ArgumentException">Thrown when the enum name is not found in registered assemblies.</exception>
    EnumResponseDto GetEnumValues(string enumName);

    /// <summary>
    /// Gets a list of all enum names registered in the service.
    /// </summary>
    /// <returns>A list of enum type names available for lookup.</returns>
    List<string> GetAllRegisteredEnums();
}
