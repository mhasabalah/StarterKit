using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Domains.Plans.Exceptions;

namespace Subscriptions.Domains.Plans.Features.DeletePlanFeature;

public class DeletePlanFeatureHandler(SubscriptionsDbContext dbContext)
    : ICommandHandler<DeletePlanFeatureCommand, Unit>
{
    public async Task<Unit> Handle(DeletePlanFeatureCommand command, CancellationToken cancellationToken)
    {
        var planExists = await dbContext.Plans
            .AsNoTracking()
            .AnyAsync(x => x.Id == command.PlanId && !x.IsDeleted, cancellationToken);

        if (!planExists)
            throw new PlanNotFoundException(command.PlanId);

        var feature = await dbContext.PlanFeatures
            .FirstOrDefaultAsync(x => x.PlanId == command.PlanId && x.Key == command.Key, cancellationToken)
            ?? throw new PlanFeatureNotFoundException(command.Key);

        dbContext.PlanFeatures.Remove(feature);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
