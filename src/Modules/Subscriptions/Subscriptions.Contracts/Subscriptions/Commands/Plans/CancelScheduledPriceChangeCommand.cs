using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record CancelScheduledPriceChangeCommand(
    Guid PlanId,
    Guid AdminId) : ICommand<Unit>;
