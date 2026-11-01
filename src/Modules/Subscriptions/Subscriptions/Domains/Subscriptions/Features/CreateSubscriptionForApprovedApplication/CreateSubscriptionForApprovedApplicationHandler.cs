using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.CreateSubscriptionForApprovedApplication;

public class CreateSubscriptionForApprovedApplicationHandler(
    IPlanRepository planRepository,
    ISubscriptionRepository subscriptionRepository,
    IInvoiceService invoiceService,
    SubscriptionsDbContext dbContext,
    IDistributedCache cache,
    ILogger<CreateSubscriptionForApprovedApplicationHandler> logger)
    : ICommandHandler<CreateSubscriptionForApprovedApplicationCommand, Guid>
{
    public async Task<Guid> Handle(
        CreateSubscriptionForApprovedApplicationCommand command,
        CancellationToken cancellationToken)
    {
        // Idempotency: return existing subscription if already active
        if (await subscriptionRepository.HasActiveSubscriptionAsync(command.SubscriberId, cancellationToken))
        {
            logger.LogWarning(
                "Subscriber {SubscriberId} already has an active subscription; skipping auto-activation",
                command.SubscriberId);
            var existing = await subscriptionRepository.GetActiveBySubscriberIdAsync(
                command.SubscriberId, cancellationToken);
            return existing!.Id;
        }

        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.PlanId);

        var idempotencyToken = $"auto-approval-{command.SubscriberId:N}";

        var subscription = Subscription.Create(
            command.SubscriberId,
            plan,
            command.FeaturesJson,
            idempotencyToken);

        var payment = SubscriptionPayment.Create(
            subscription.Id,
            command.Price,
            command.Currency);

        if (command.ProofAttachmentId.HasValue)
        {
            payment.SubmitProofWithSnapshot(
                command.ProofAttachmentId.Value,
                command.PaymentMethodId,
                command.PaymentMethodType,
                command.PaymentMethodDisplayName,
                command.PaymentMethodValue,
                command.PaymentMethodDescription,
                command.SubscriberId);
        }

        subscription.Activate();

        if (payment.Status == PaymentStatus.Submitted)
            payment.Approve(command.AdminId, command.SubscriberId);

        await subscriptionRepository.AddAsync(subscription, cancellationToken);
        await dbContext.Payments.AddAsync(payment, cancellationToken);

        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                SubscriptionStatus.PendingApproval,
                SubscriptionStatus.Active,
                command.AdminId,
                "Admin",
                null),
            cancellationToken);

        await dbContext.AdminAuditLogs.AddAsync(
            AdminActionAuditLog.Create(
                command.AdminId,
                "ApproveApplicationSubscription",
                subscription.Id,
                "Subscription",
                subscription.SubscriberId,
                null),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);

        await invoiceService.GenerateActivationInvoiceAsync(subscription, payment.Id, cancellationToken);

        logger.LogInformation(
            "Auto-activated subscription {SubscriptionId} for subscriber {SubscriberId} on plan {PlanName}",
            subscription.Id, subscription.SubscriberId, plan.Name);

        return subscription.Id;
    }
}
