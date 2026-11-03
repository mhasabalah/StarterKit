using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.RejectRenewalPayment;

public class RejectRenewalPaymentCommandValidator : AbstractValidator<RejectRenewalPaymentCommand>
{
    public RejectRenewalPaymentCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public class RejectRenewalPaymentHandler(
    ISubscriptionRepository subscriptionRepository,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<RejectRenewalPaymentCommand, Unit>
{
    public async Task<Unit> Handle(
        RejectRenewalPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        dbContext.ExpectVersion(subscription, command.Version);

        if (subscription.Status != SubscriptionStatus.PastDue)
            throw new ConflictException("Subscription is not past due.");

        var payment = await PaymentSelection.ResolveSubmittedAsync(
            subscriptionRepository, subscription.Id, command.PaymentId, PaymentPurpose.Renewal, cancellationToken);

        payment.Reject(command.AdminId, command.Reason, subscription.SubscriberId, DateTimeOffset.UtcNow);

        audit.Record(
            "RejectRenewal",
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
