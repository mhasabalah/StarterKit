using System.Text.Json;
using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.UpgradeSubscription;

public class UpgradeSubscriptionCommandValidator : AbstractValidator<UpgradeSubscriptionCommand>
{
    public UpgradeSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.SubscriberId).NotEmpty();
        RuleFor(x => x.NewPlanId).NotEmpty();
    }
}

public class UpgradeSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IPlanRepository planRepository,
    IInvoiceService invoiceService,
    IUpgradeTrustService trustService,
    IDistributedCache cache,
    SubscriptionsDbContext dbContext,
    ILogger<UpgradeSubscriptionHandler> logger)
    : ICommandHandler<UpgradeSubscriptionCommand, UpgradeSubscriptionResult>
{
    public async Task<UpgradeSubscriptionResult> Handle(
        UpgradeSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdForUpdateAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.SubscriberId != command.SubscriberId)
            throw new ForbiddenException("You are not allowed to upgrade this subscription.");

        if (subscription.Status != SubscriptionStatus.Active)
            throw new ConflictException("Only active subscriptions can be upgraded.");

        var newPlan = await planRepository.GetByIdAsync(command.NewPlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.NewPlanId);

        if (!newPlan.IsActive || newPlan.IsDeleted)
            throw new PlanNotFoundException(command.NewPlanId);

        if (newPlan.Price <= subscription.Price)
            throw new ConflictException("New plan price must be higher than the current plan price.");

        var evaluation = await trustService.EvaluateAsync(subscription, newPlan, cancellationToken);

        var originalPlanId = subscription.PlanId;
        var newFeaturesJson = JsonSerializer.Serialize(
            newPlan.Features.ToDictionary(x => x.Key, _ => "true"));

        subscription.UpgradePlan(newPlan, newFeaturesJson, command.SubscriberId, evaluation.ProratedAmount, evaluation.IsDeferred);

        Guid? prorationPaymentId = null;
        if (!evaluation.IsDeferred && evaluation.ProratedAmount > 0)
        {
            var prorationPayment = SubscriptionPayment.Create(
                subscription.Id,
                evaluation.ProratedAmount,
                subscription.Currency);
            // Must use AddAsync (not subscription.AddPayment) — EF infers Modified for a
            // client-assigned Guid added to an unloaded collection navigation property.
            await dbContext.Payments.AddAsync(prorationPayment, cancellationToken);
            prorationPaymentId = prorationPayment.Id;
        }

        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                SubscriptionStatus.Active,
                SubscriptionStatus.Active,
                command.SubscriberId,
                "Subscriber",
                $"Upgraded to plan {newPlan.Name}"),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);

        var invoice = await invoiceService.GenerateProratedInvoiceAsync(
            subscription,
            prorationPaymentId,
            evaluation.ProratedAmount,
            evaluation.IsDeferred,
            originalPlanId,
            cancellationToken);

        logger.LogInformation(
            "Subscription {SubscriptionId} upgraded to plan {PlanName}. Proration={Amount} Deferred={IsDeferred}",
            subscription.Id, newPlan.Name, evaluation.ProratedAmount, evaluation.IsDeferred);

        return new UpgradeSubscriptionResult(
            subscription.Id,
            newPlan.Name.En,
            evaluation.ProratedAmount,
            subscription.Currency,
            evaluation.IsDeferred,
            invoice.Id,
            evaluation.ProofRequiredBy);
    }
}
