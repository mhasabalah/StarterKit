using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Commands;

public record ScheduleDowngradeCommand(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid NewPlanId) : ICommand<ScheduleDowngradeResult>;

public record ScheduleDowngradeResult(
    Guid SubscriptionId,
    string CurrentPlanName,
    string NewPlanName,
    DateTimeOffset EffectiveAt);
