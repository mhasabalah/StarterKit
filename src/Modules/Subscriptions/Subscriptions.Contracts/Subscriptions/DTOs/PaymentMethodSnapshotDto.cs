namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record PaymentMethodSnapshotDto(
    Guid? Id,
    string? Type,
    string? DisplayName,
    string? Value,
    string? Description);
