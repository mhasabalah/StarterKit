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

/// <summary>
/// Schedules a downgrade for the renewal boundary (applied by the renewal orchestrator before the renewal is priced).
/// Several downgrades over time are fine; only one may be pending (filtered unique index).
/// </summary>
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
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.SubscriberId != command.SubscriberId)
            throw new ForbiddenException("You are not allowed to schedule a downgrade for this subscription.");

        var newPlan = await planRepository.GetByIdAsync(command.NewPlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.NewPlanId);

        if (!newPlan.IsActive || newPlan.IsDeleted)
            throw new PlanNotFoundException(command.NewPlanId);

        var now = DateTimeOffset.UtcNow;
        var currentPlanName = subscription.PlanName;
        var change = subscription.ScheduleDowngrade(
            PlanSnapshot.From(newPlan, PricingPolicy.PlanPriceAt(newPlan, subscription.RenewalDate ?? now)),
            command.SubscriberId,
            now);

        dbContext.ScheduledPlanChanges.Add(change);
        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            subscription.SubscriberId,
            SubscriptionStatus.Active,
            SubscriptionStatus.Active,
            command.SubscriberId,
            "Subscriber",
            $"Scheduled downgrade to plan {newPlan.Name.En}"));

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ScheduleDowngradeResult(
            subscription.Id,
            currentPlanName,
            newPlan.Name.En,
            change.EffectiveAt);
    }
}
