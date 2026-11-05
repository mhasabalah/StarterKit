namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <summary>Usage of one metered plan feature in the current period.</summary>
/// <param name="Limit">Null = unlimited.</param>
/// <param name="ResetPeriod">Monthly or Never.</param>
/// <param name="PeriodKey">The counter period (<c>2026-11</c> or <c>all</c>).</param>
/// <param name="ResetsAt">Start of the next period; null for Never.</param>
public record UsageDto(
    string Key,
    long Used,
    long? Limit,
    string ResetPeriod,
    string PeriodKey,
    DateTimeOffset? ResetsAt);
