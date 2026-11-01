using Shared.Contracts.Localization;

namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record CreateFeatureDefinitionCommand(
    string Key,
    LocalizedText DisplayName,
    LocalizedText? Description,
    string? Category,
    int SortOrder) : ICommand<string>;
