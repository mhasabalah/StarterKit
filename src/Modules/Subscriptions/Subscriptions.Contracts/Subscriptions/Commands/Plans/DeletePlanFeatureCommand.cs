namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record DeletePlanFeatureCommand(Guid PlanId, string Key) : ICommand;
