using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.CreateSubscriptionForApprovedApplication;

/// <summary>
/// Cross-module entry point: another module approved an application and wants an active subscription right away.
/// Idempotent per subscriber (returns the open subscription if one exists; the unique index settles races).
/// The activation invoice is issued in the same transaction when the payment is approved here.
/// </summary>
public class CreateSubscriptionForApprovedApplicationHandler(
    IPlanRepository planRepository,
    ISubscriptionRepository subscriptionRepository,
    IInvoiceIssuer invoiceIssuer,
    ILedgerRepository ledger,
    ISubscriberInfoProvider subscriberInfoProvider,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext,
    IDistributedCache cache,
    ILogger<CreateSubscriptionForApprovedApplicationHandler> logger)
    : ICommandHandler<CreateSubscriptionForApprovedApplicationCommand, Guid>
{
    public async Task<Guid> Handle(
        CreateSubscriptionForApprovedApplicationCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await subscriptionRepository.GetOpenBySubscriberIdAsync(command.SubscriberId, cancellationToken);
        if (existing is not null)
        {
            logger.LogWarning(
                "Subscriber {SubscriberId} already has an open subscription; skipping auto-activation",
                command.SubscriberId);
            return existing.Id;
        }

        var plan = await planRepository.GetByIdAsync(command.PlanId, cancellationToken)
            ?? throw new PlanNotFoundException(command.PlanId);

        var now = DateTimeOffset.UtcNow;

        // The caller dictates the commercial terms (approved application); limits/features come from the command.
        var snapshot = new PlanSnapshot(
            plan.Id,
            plan.Name.En,
            command.Price,
            command.Currency,
            command.BillingCycleInMonths,
            command.MaxItems,
            command.MaxStorageGb,
            command.MaxUsers,
            string.IsNullOrWhiteSpace(command.FeaturesJson) ? FeaturesJson.Empty : command.FeaturesJson);

        // The approved application states the amount actually collected (gross).
        var subscription = Subscription.Create(command.SubscriberId, snapshot, command.Price, now);
        var payment = subscription.ActivationPayment;

        if (command.ProofAttachmentId.HasValue)
        {
            payment.SubmitProof(
                command.ProofAttachmentId.Value,
                new PaymentMethodSnapshot(
                    command.PaymentMethodId,
                    command.PaymentMethodType,
                    command.PaymentMethodDisplayName,
                    command.PaymentMethodValue,
                    command.PaymentMethodDescription),
                command.SubscriberId,
                now);
        }

        subscription.Activate(now);
        await PaymentSettlementService.RefreshSubscriberSnapshotAsync(subscriberInfoProvider, subscription, cancellationToken);

        SubscriptionInvoice? invoice = null;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await subscriptionRepository.AddAsync(subscription, cancellationToken);

            if (payment.Status == PaymentStatus.Submitted)
            {
                var received = payment.Approve(command.AdminId, command.SubscriberId, now);
                invoice = await invoiceIssuer.IssueForPaymentAsync(subscription, payment, cancellationToken);
                if (received > 0m)
                {
                    ledger.Add(LedgerEntry.ForPayment(subscription, payment, received, invoice.Id, command.AdminId, now));
                }
            }

            dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                SubscriptionStatus.PendingApproval,
                SubscriptionStatus.Active,
                command.AdminId,
                "Admin",
                null));

            audit.Record(
                "ApproveApplicationSubscription",
                subscription.Id,
                "Subscription",
                subscription.SubscriberId,
                amount: payment.Amount,
                currency: payment.Currency,
                after: new { Status = subscription.Status.ToString(), PaymentStatus = payment.Status.ToString(), InvoiceId = invoice?.Id },
                adminId: command.AdminId);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), cancellationToken);
        if (invoice is not null)
        {
            invoiceIssuer.EnqueueDelivery(invoice.Id);
        }

        logger.LogInformation(
            "Auto-activated subscription {SubscriptionId} for subscriber {SubscriberId} on plan {PlanName}",
            subscription.Id, subscription.SubscriberId, plan.Name.En);

        return subscription.Id;
    }
}
