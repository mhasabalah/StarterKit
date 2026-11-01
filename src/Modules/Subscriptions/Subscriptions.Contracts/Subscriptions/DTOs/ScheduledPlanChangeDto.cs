namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record ScheduledPlanChangeDto(
    string NewPlanName,
    decimal NewPrice,
    string Currency,
    DateTimeOffset EffectiveAt);
