namespace Subscriptions.Contracts.Subscriptions.Commands;

public record AddPaymentMethodCommand(
    string Type,
    string DisplayName,
    string Value,
    string? Description,
    string? Metadata,
    int SortOrder) : ICommand<Guid>;
