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

/// <summary>
/// Immediate upgrade with a prorated charge for the rest of the current period.
/// <list type="bullet">
/// <item>Same currency and billing cycle only (400 otherwise); a pending scheduled downgrade is cancelled.</item>
/// <item>Deferred (trusted + small): added to the next renewal bill, no payment/invoice now.</item>
/// <item>Otherwise: a Proration payment + its invoice are created in the same transaction. The subscriber uploads a
/// proof (POST /subscriptions/{id}/proration/proof) and an admin approves it; if it is still unpaid after the SLA the
/// upgrade is reverted and that invoice is credited.</item>
/// </list>
/// </summary>
public class UpgradeSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IPlanRepository planRepository,
    IInvoiceIssuer invoiceIssuer,
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
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.SubscriberId != command.SubscriberId)
            throw new ForbiddenException("You are not allowed to upgrade this subscription.");

        if (subscription.Status != SubscriptionStatus.Active)
            throw new ConflictException("Only active subscriptions can be upgraded.");

        var newPlan = await planRepository.GetByIdAsync(command.NewPlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.NewPlanId);

        if (!newPlan.IsActive || newPlan.IsDeleted)
            throw new PlanNotFoundException(command.NewPlanId);

        // Loads still-owed payments into Subscription.Payments so the aggregate can refuse stacked unpaid upgrades.
        await subscriptionRepository.GetAwaitingPaymentsAsync(subscription.Id, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var quote = await trustService.QuoteAsync(subscription, newPlan, now, cancellationToken);
        var originalPlanName = subscription.PlanName;

        var payment = subscription.UpgradePlan(
            PlanSnapshot.From(newPlan, quote.NewPlanPrice),
            quote.ProratedAmount,
            quote.IsDeferred,
            quote.AmountDue,
            now);

        SubscriptionInvoice? invoice = null;
        if (payment is not null)
        {
            dbContext.Payments.Add(payment);
            invoice = await invoiceIssuer.IssueForPaymentAsync(subscription, payment, cancellationToken);
        }

        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            subscription.SubscriberId,
            SubscriptionStatus.Active,
            SubscriptionStatus.Active,
            command.SubscriberId,
            "Subscriber",
            $"Upgraded from plan {originalPlanName} to plan {newPlan.Name.En}"));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);
        if (invoice is not null)
        {
            invoiceIssuer.EnqueueDelivery(invoice.Id);
        }

        logger.LogInformation(
            "Subscription {SubscriptionId} upgraded to plan {PlanName}. Proration={Amount} Due={Due} Deferred={IsDeferred}",
            subscription.Id, newPlan.Name.En, quote.ProratedAmount, quote.AmountDue, quote.IsDeferred);

        return new UpgradeSubscriptionResult(
            subscription.Id,
            newPlan.Name.En,
            quote.ProratedAmount,
            payment?.Amount ?? 0m,
            subscription.Currency,
            quote.IsDeferred,
            payment?.Id,
            invoice?.Id,
            payment is null ? null : quote.ProofRequiredBy);
    }
}
