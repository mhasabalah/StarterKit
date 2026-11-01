namespace Shared.Contracts.Enums;

/// <summary>
/// Represents a single enum value with bilingual localization.
/// </summary>
public record EnumItemDto
{
    /// <summary>
    /// Gets the numeric value of the enum.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Gets the enum member name (e.g., "House", "Apartment").
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Gets the English description from [LocalizedDescription] attribute or falls back to Key.
    /// </summary>
    public string NameEn { get; init; } = string.Empty;

    /// <summary>
    /// Gets the Arabic description from [LocalizedDescription] attribute or falls back to Key.
    /// </summary>
    public string NameAr { get; init; } = string.Empty;
}

/// <summary>
/// Represents a complete enum response with all its values.
/// </summary>
public record EnumResponseDto
{
    /// <summary>
    /// Gets the name of the enum type (e.g., "AttachmentType").
    /// </summary>
    public string EnumName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the number of enum values.
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// Gets the list of enum items with bilingual descriptions.
    /// </summary>
    public List<EnumItemDto> Items { get; init; } = new();
}
