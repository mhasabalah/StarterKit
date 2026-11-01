using Subscriptions.Contracts.Subscriptions.Commands;

namespace Subscriptions.Domains.Subscriptions.Features.RejectSubscription;

public class RejectSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<RejectSubscriptionCommand, Unit>
{
    public async Task<Unit> Handle(RejectSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.Status != SubscriptionStatus.PendingApproval)
        {
            throw new ConflictException("Subscription is not pending approval.");
        }

        var latestPayment = subscription.Payments.OrderByDescending(x => x.CreatedAt).FirstOrDefault()
            ?? throw new PaymentNotFoundException(command.SubscriptionId);
        subscription.Reject(command.Reason);

        if (latestPayment.Status == PaymentStatus.Submitted)
        {
            latestPayment.Reject(command.AdminId, command.Reason ?? "Rejected by admin", subscription.SubscriberId);
        }

        await dbContext.TransitionLogs.AddAsync(
            SubscriptionTransitionLog.Create(
                subscription.Id,
                subscription.SubscriberId,
                SubscriptionStatus.PendingApproval,
                SubscriptionStatus.Rejected,
                command.AdminId,
                "Admin",
                command.Reason),
            cancellationToken);

        await dbContext.AdminAuditLogs.AddAsync(
            AdminActionAuditLog.Create(
                command.AdminId,
                "RejectSubscription",
                subscription.Id,
                "Subscription",
                subscription.SubscriberId,
                command.Reason),
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
