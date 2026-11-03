using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.RejectSubscription;

public class RejectSubscriptionCommandValidator : AbstractValidator<RejectSubscriptionCommand>
{
    public RejectSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}

public class RejectSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<RejectSubscriptionCommand, Unit>
{
    public async Task<Unit> Handle(RejectSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        dbContext.ExpectVersion(subscription, command.Version);

        if (subscription.Status != SubscriptionStatus.PendingApproval)
            throw new ConflictException("Subscription is not pending approval.");

        var now = DateTimeOffset.UtcNow;
        var reason = command.Reason ?? "Rejected by admin";

        // Rejecting the subscription closes its activation payment: reject a submitted proof, fail an unpaid one.
        var awaiting = (await subscriptionRepository.GetAwaitingPaymentsAsync(subscription.Id, cancellationToken))
            .Where(x => x.Purpose == PaymentPurpose.Activation)
            .ToList();

        if (command.PaymentId is { } paymentId && awaiting.All(x => x.Id != paymentId))
            throw new PaymentNotFoundException(paymentId);

        subscription.Reject(command.Reason, now);
        foreach (var payment in awaiting)
        {
            if (payment.Status == PaymentStatus.Submitted)
                payment.Reject(command.AdminId, reason, subscription.SubscriberId, now);
            payment.MarkFailed(reason);
        }

        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            subscription.SubscriberId,
            SubscriptionStatus.PendingApproval,
            SubscriptionStatus.Rejected,
            command.AdminId,
            "Admin",
            command.Reason));

        audit.Record(
            "RejectSubscription",
            subscription.Id,
            "Subscription",
            subscription.SubscriberId,
            command.Reason,
            amount: awaiting.FirstOrDefault()?.Amount,
            currency: subscription.Currency,
            before: new { Status = SubscriptionStatus.PendingApproval.ToString() },
            after: new { Status = subscription.Status.ToString() },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
