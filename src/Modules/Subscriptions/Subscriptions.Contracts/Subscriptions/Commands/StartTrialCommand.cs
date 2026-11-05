namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// Starts a free trial on a plan that offers one (<c>Plan.TrialDays</c> &gt; 0). No payment proof is needed. Each
/// subscriber gets ONE trial ever (409 afterwards).
/// </summary>
public record StartTrialCommand(Guid SubscriberId, Guid PlanId) : ICommand<StartTrialResult>;

public record StartTrialResult(
    Guid SubscriptionId,
    string Status,
    string PlanName,
    DateTimeOffset TrialEndsAt);
