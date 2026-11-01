using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Domains.Plans.Exceptions;

namespace Subscriptions.Domains.Plans.Features.DeletePlan;

public class DeletePlanHandler(IPlanRepository planRepository)
    : ICommandHandler<DeletePlanCommand, Unit>
{
    public async Task<Unit> Handle(DeletePlanCommand command, CancellationToken cancellationToken)
    {
        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.PlanId);

        var hasActive = await planRepository.HasActiveSubscriptionsAsync(command.PlanId, cancellationToken);
        if (hasActive)
        {
            throw new PlanHasActiveSubscriptionsException();
        }

        plan.SoftDelete();
        await planRepository.UpdateAsync(plan, cancellationToken);
        return Unit.Value;
    }
}
