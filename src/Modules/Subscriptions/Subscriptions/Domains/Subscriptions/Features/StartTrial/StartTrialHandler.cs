using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.StartTrial;

public class StartTrialCommandValidator : AbstractValidator<StartTrialCommand>
{
    public StartTrialCommandValidator()
    {
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
    }
}

/// <summary>One trial per subscriber, ever; the plan must offer one. No payment, no approval: Trialing right away.</summary>
public class StartTrialHandler(
    IPlanRepository planRepository,
    ISubscriptionRepository subscriptionRepository,
    SubscriptionsDbContext dbContext,
    ISubscriberInfoProvider subscriberInfoProvider,
    TimeProvider timeProvider)
    : ICommandHandler<StartTrialCommand, StartTrialResult>
{
    public const string TrialAlreadyUsedCode = "trial_already_used";
    public const string PlanHasNoTrialCode = "plan_has_no_trial";

    public async Task<StartTrialResult> Handle(StartTrialCommand command, CancellationToken cancellationToken)
    {
        if (await subscriptionRepository.HasOpenSubscriptionAsync(command.SubscriberId, cancellationToken))
            throw new ActiveSubscriptionAlreadyExistsException();

        if (await subscriptionRepository.HasUsedTrialAsync(command.SubscriberId, cancellationToken))
            throw new ConflictException("This subscriber has already used a free trial.") { ErrorCode = TrialAlreadyUsedCode };

        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken);
        if (plan is null || !plan.IsActive || !plan.IsPublic || plan.IsDeleted)
            throw new PlanNotFoundException(command.PlanId);

        if (plan.TrialDays < 1)
            throw new ConflictException("This plan does not offer a free trial.") { ErrorCode = PlanHasNoTrialCode };

        var now = timeProvider.GetUtcNow();
        var subscription = Subscription.StartTrial(
            command.SubscriberId,
            PlanSnapshot.From(plan, PricingPolicy.PlanPriceAt(plan, now)),
            plan.TrialDays,
            now);
        await PaymentSettlementService.RefreshSubscriberSnapshotAsync(subscriberInfoProvider, subscription, cancellationToken);

        await subscriptionRepository.AddAsync(subscription, cancellationToken);
        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            command.SubscriberId,
            null,
            SubscriptionStatus.Trialing,
            command.SubscriberId,
            "Subscriber",
            $"Free trial ({plan.TrialDays} days)"));

        await dbContext.SaveChangesAsync(cancellationToken);

        return new StartTrialResult(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.PlanName,
            subscription.TrialEndsAt!.Value);
    }
}
