using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.RejectRenewalPayment;

public class RejectRenewalPaymentHandler(
    ISubscriptionRepository subscriptionRepository,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<RejectRenewalPaymentCommand, Unit>
{
    public async Task<Unit> Handle(
        RejectRenewalPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.Status != SubscriptionStatus.PastDue)
        {
            throw new ConflictException("Subscription is not past due.");
        }

        var payment = subscription.Payments.FirstOrDefault(x => x.Id == command.PaymentId)
            ?? throw new PaymentNotFoundException(command.PaymentId);
        if (payment.Status != PaymentStatus.Submitted)
        {
            throw new ConflictException("Payment is not submitted.");
        }

        payment.Reject(command.AdminId, command.Reason, subscription.SubscriberId);
        await dbContext.AdminAuditLogs.AddAsync(
            AdminActionAuditLog.Create(
                command.AdminId,
                "RejectRenewal",
                subscription.Id,
                "Subscription",
                subscription.SubscriberId,
                command.Reason),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
