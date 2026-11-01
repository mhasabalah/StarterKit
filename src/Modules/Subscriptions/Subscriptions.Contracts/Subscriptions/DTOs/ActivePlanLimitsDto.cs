namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record ActivePlanLimitsDto(
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    string Status,
    DateTimeOffset? GracePeriodEndsAt);
