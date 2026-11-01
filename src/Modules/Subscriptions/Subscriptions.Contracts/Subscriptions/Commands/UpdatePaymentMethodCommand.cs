namespace Subscriptions.Contracts.Subscriptions.Commands;

public record UpdatePaymentMethodCommand(
    Guid Id,
    string Type,
    string DisplayName,
    string Value,
    string? Description,
    string? Metadata,
    int SortOrder,
    bool IsActive,
    long? Version = null) : ICommand<Unit>;
