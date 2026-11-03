using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.RejectProrationPayment;

public class RejectProrationPaymentCommandValidator : AbstractValidator<RejectProrationPaymentCommand>
{
    public RejectProrationPaymentCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

/// <summary>Rejects a proration proof; the subscriber can upload another until the upgrade SLA elapses.</summary>
public class RejectProrationPaymentHandler(
    ISubscriptionRepository subscriptionRepository,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<RejectProrationPaymentCommand, Unit>
{
    public async Task<Unit> Handle(RejectProrationPaymentCommand command, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.Status != SubscriptionStatus.Active)
            throw new ConflictException("Proration payments can only be reviewed for active subscriptions.");

        var payment = await PaymentSelection.ResolveSubmittedAsync(
            subscriptionRepository, subscription.Id, command.PaymentId, PaymentPurpose.Proration, cancellationToken);

        dbContext.ExpectVersion(payment, command.Version);

        payment.Reject(command.AdminId, command.Reason, subscription.SubscriberId, DateTimeOffset.UtcNow);

        audit.Record(
            "RejectProrationPayment",
            payment.Id,
            "Payment",
            subscription.SubscriberId,
            command.Reason,
            payment.Amount,
            payment.Currency,
            before: new { PaymentStatus = PaymentStatus.Submitted.ToString() },
            after: new { PaymentStatus = payment.Status.ToString() },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
