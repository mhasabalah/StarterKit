using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.ApproveRenewal;

public class ApproveRenewalHandler(
    ISubscriptionRepository subscriptionRepository,
    IPlanRepository planRepository,
    IInvoiceService invoiceService,
    IDistributedCache cache,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<ApproveRenewalCommand, ApproveRenewalResult>
{
    public async Task<ApproveRenewalResult> Handle(
        ApproveRenewalCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.Status != SubscriptionStatus.PastDue)
        {
            throw new ConflictException("Subscription is not past due.");
        }

        if (subscription.SubscriberId == command.AdminId)
        {
            throw new SelfApprovalNotAllowedException();
        }

        var latestPayment = subscription.Payments.OrderByDescending(x => x.CreatedAt).FirstOrDefault()
            ?? throw new PaymentNotFoundException(command.SubscriptionId);
        if (latestPayment.Status != PaymentStatus.Submitted)
        {
            throw new ConflictException("Latest payment is not submitted.");
        }

        // Determine effective price — covers both the race-window (PendingPrice not yet promoted)
        // and the post-promotion case (plan.Price already advanced, PendingPrice cleared).
        var plan = await planRepository.GetByIdAsync(subscription.PlanId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        decimal effectivePrice;
        if (plan is { PendingPrice: not null, PriceEffectiveAt: not null } && plan.PriceEffectiveAt <= now)
            effectivePrice = plan.PendingPrice.Value;   // effective but job hasn't promoted yet
        else if (plan != null && plan.Price != subscription.Price)
            effectivePrice = plan.Price;                // already promoted by ProcessPlanPriceChangesJob
        else
            effectivePrice = subscription.Price;

        if (latestPayment.Amount != effectivePrice ||
            !string.Equals(latestPayment.Currency, subscription.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentAmountMismatchException(
                subscription.Id,
                effectivePrice,
                subscription.Currency,
                latestPayment.Amount,
                latestPayment.Currency);
        }

        subscription.Renew();

        // Sync the subscription's price snapshot so future MarkPastDue uses the correct amount.
        if (effectivePrice != subscription.Price)
            subscription.UpdatePrice(effectivePrice);

        latestPayment.Approve(command.AdminId, subscription.SubscriberId);

        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                SubscriptionStatus.PastDue,
                SubscriptionStatus.Active,
                command.AdminId,
                "Admin",
                null),
            cancellationToken);

        await dbContext.AdminAuditLogs.AddAsync(
            AdminActionAuditLog.Create(
                command.AdminId,
                "ApproveRenewal",
                subscription.Id,
                "Subscription",
                subscription.SubscriberId,
                null),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);

        await invoiceService.GenerateRenewalInvoiceAsync(subscription, latestPayment.Id, cancellationToken);

        return new ApproveRenewalResult(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.RenewalDate!.Value,
            Guid.NewGuid().ToString());
    }
}
