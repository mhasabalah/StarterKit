using Shared.Contracts.Localization;

namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record AdminPlanFeatureDto(string Key, LocalizedText DisplayName, bool IsAssigned);
