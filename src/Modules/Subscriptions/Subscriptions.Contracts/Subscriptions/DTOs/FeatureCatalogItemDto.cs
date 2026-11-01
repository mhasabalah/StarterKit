using Shared.Contracts.Localization;

namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record FeatureCatalogItemDto(
    string Key,
    LocalizedText DisplayName,
    LocalizedText? Description,
    string? Category,
    int SortOrder,
    bool IsActive);
