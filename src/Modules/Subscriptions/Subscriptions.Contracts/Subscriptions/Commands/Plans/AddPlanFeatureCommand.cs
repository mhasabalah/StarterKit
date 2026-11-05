using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

/// <param name="Limit">Metered feature: the quota per reset period; null = a plain on/off feature.</param>
/// <param name="ResetPeriod">Only with a limit; defaults to Monthly.</param>
public record AddPlanFeatureCommand(Guid PlanId, string Key, long? Limit = null, QuotaResetPeriod? ResetPeriod = null)
    : ICommand<Guid>;
