using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Subscriptions.Domains.Plans.Exceptions;

namespace Subscriptions.Domains.Plans.Features.AddPlanFeature;

public class AddPlanFeatureHandler(
    SubscriptionsDbContext dbContext,
    IAdminAuditWriter audit)
    : ICommandHandler<AddPlanFeatureCommand, Guid>
{
    public async Task<Guid> Handle(AddPlanFeatureCommand command, CancellationToken cancellationToken)
    {
        var definitionExists = await dbContext.FeatureDefinitions
            .AnyAsync(x => x.Key == command.Key && x.IsActive, cancellationToken);

        if (!definitionExists)
            throw new BadRequestException($"Feature '{command.Key}' does not exist in the catalog. Create it first via POST /admin/feature-definitions.");

        var planExists = await dbContext.Plans
            .AsNoTracking()
            .AnyAsync(x => x.Id == command.PlanId && !x.IsDeleted, cancellationToken);

        if (!planExists)
            throw new PlanNotFoundException(command.PlanId);

        var alreadyAssigned = await dbContext.PlanFeatures
            .AnyAsync(x => x.PlanId == command.PlanId && x.Key == command.Key, cancellationToken);

        if (alreadyAssigned)
            throw new ConflictException($"Feature '{command.Key}' is already assigned to this plan.");

        var feature = PlanFeature.Create(command.PlanId, command.Key);
        await dbContext.PlanFeatures.AddAsync(feature, cancellationToken);
        audit.Record("AddPlanFeature", command.PlanId, "Plan", after: new { command.Key });
        await dbContext.SaveChangesAsync(cancellationToken);

        return feature.Id;
    }
}
