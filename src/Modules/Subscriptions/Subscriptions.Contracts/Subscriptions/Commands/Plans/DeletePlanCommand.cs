namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record DeletePlanCommand(Guid PlanId) : ICommand;
