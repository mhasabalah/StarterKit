using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record SchedulePriceChangeCommand(
    Guid PlanId,
    Guid AdminId,
    decimal NewPrice,
    DateTimeOffset EffectiveAt) : ICommand<Unit>;
