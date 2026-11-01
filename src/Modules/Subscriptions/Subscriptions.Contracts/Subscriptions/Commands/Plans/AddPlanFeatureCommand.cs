namespace Subscriptions.Contracts.Subscriptions.Commands.Plans;

public record AddPlanFeatureCommand(Guid PlanId, string Key) : ICommand<Guid>;
