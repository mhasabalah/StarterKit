using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Domains.Plans.Exceptions;

namespace Subscriptions.Domains.Plans.Features.DeletePlan;

public class DeletePlanHandler(IPlanRepository planRepository, IAdminAuditWriter audit)
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
        audit.Record("DeletePlan", plan.Id, "Plan", amount: plan.Price, currency: plan.Currency,
            before: new { Name = plan.Name.En, plan.Price, plan.Currency });
        await planRepository.UpdateAsync(plan, cancellationToken);
        return Unit.Value;
    }
}
