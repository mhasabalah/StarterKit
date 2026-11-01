namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record PaymentMethodDto(
    Guid Id,
    string Type,
    string DisplayName,
    string Value,
    string? Description,
    string? Metadata,
    int SortOrder,
    bool IsActive,
    long Version);

public record PaymentInstructionsDto(
    string SupportWhatsApp,
    string SupportEmail);
