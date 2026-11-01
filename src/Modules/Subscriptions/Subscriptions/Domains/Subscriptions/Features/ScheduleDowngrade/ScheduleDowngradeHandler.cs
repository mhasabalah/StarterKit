using System.Text.Json;
using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.ScheduleDowngrade;

public class ScheduleDowngradeCommandValidator : AbstractValidator<ScheduleDowngradeCommand>
{
    public ScheduleDowngradeCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.NewPlanId).NotEmpty();
    }
}

public class ScheduleDowngradeHandler(
    ISubscriptionRepository subscriptionRepository,
    IPlanRepository planRepository,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<ScheduleDowngradeCommand, ScheduleDowngradeResult>
{
    public async Task<ScheduleDowngradeResult> Handle(
        ScheduleDowngradeCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdForUpdateAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.SubscriberId != command.SubscriberId)
            throw new ForbiddenException("You are not allowed to schedule a downgrade for this subscription.");

        if (subscription.Status != SubscriptionStatus.Active)
            throw new ConflictException("Only active subscriptions can have a downgrade scheduled.");

        var newPlan = await planRepository.GetByIdAsync(command.NewPlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.NewPlanId);

        if (!newPlan.IsActive || newPlan.IsDeleted)
            throw new PlanNotFoundException(command.NewPlanId);

        if (newPlan.Price >= subscription.Price)
            throw new ConflictException("New plan price must be lower than the current plan price for a downgrade.");

        if (subscription.ScheduledPlanChange is { Status: PlanChangeStatus.Pending })
            throw new ConflictException("A pending scheduled downgrade already exists. Cancel it first.");

        var currentPlanName = subscription.PlanName;

        var newFeaturesJson = JsonSerializer.Serialize(
            newPlan.Features.ToDictionary(x => x.Key, _ => "true"));

        subscription.ScheduleDowngrade(newPlan, newFeaturesJson, command.SubscriberId);

        // Must use AddAsync — EF infers Modified for a client-assigned Guid set on a navigation property.
        await dbContext.ScheduledPlanChanges.AddAsync(subscription.ScheduledPlanChange!, cancellationToken);

        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                SubscriptionStatus.Active,
                SubscriptionStatus.Active,
                command.SubscriberId,
                "Subscriber",
                $"Scheduled downgrade to plan {newPlan.Name}"),
            cancellationToken);

        await subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        var effectiveAt = subscription.ScheduledPlanChange!.EffectiveAt;

        return new ScheduleDowngradeResult(
            subscription.Id,
            currentPlanName,
            newPlan.Name.En,
            effectiveAt);
    }
}
