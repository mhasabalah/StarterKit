using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record ForceApplyPriceChangeCommand(
    Guid PlanId,
    Guid AdminId,
    decimal NewPrice) : ICommand<Unit>;
