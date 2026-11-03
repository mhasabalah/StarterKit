using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.ApproveProrationPayment;

public class ApproveProrationPaymentCommandValidator : AbstractValidator<ApproveProrationPaymentCommand>
{
    public ApproveProrationPaymentCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
    }
}

/// <summary>
/// Approves the submitted proof of an upgrade proration payment (subscription stays Active). The proration invoice is
/// normally issued with the upgrade; if it is missing (legacy rows) it is issued here. The ledger entry is written in
/// the same transaction (<see cref="IPaymentSettlementService"/>).
/// </summary>
public class ApproveProrationPaymentHandler(
    ISubscriptionRepository subscriptionRepository,
    IPaymentSettlementService settlement,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<ApproveProrationPaymentCommand, ApproveProrationPaymentResult>
{
    public async Task<ApproveProrationPaymentResult> Handle(
        ApproveProrationPaymentCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        if (subscription.Status != SubscriptionStatus.Active)
            throw new ConflictException("Proration payments can only be approved for active subscriptions.");

        if (subscription.SubscriberId == command.AdminId)
            throw new SelfApprovalNotAllowedException();

        var payment = await PaymentSelection.ResolveSubmittedAsync(
            subscriptionRepository, subscription.Id, command.PaymentId, PaymentPurpose.Proration, cancellationToken);

        dbContext.ExpectVersion(payment, command.Version);
        settlement.EnsureSettleable(subscription, payment);

        var now = DateTimeOffset.UtcNow;
        var received = payment.Approve(command.AdminId, subscription.SubscriberId, now);
        var result = await settlement.SettleAsync(
            subscription, payment, received, command.AdminId, now, ct: cancellationToken);

        audit.Record(
            "ApproveProrationPayment",
            payment.Id,
            "Payment",
            subscription.SubscriberId,
            amount: received,
            currency: payment.Currency,
            before: new { PaymentStatus = PaymentStatus.Submitted.ToString() },
            after: new
            {
                PaymentStatus = payment.Status.ToString(),
                InvoiceId = result.Invoice.Id,
                result.Invoice.InvoiceNumber,
                payment.DuplicateProof,
                payment.DuplicateOfPaymentId
            },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await settlement.AfterCommitAsync(subscription, result, cancellationToken);

        return new ApproveProrationPaymentResult(
            subscription.Id, payment.Id, payment.Status.ToString(), result.Invoice.Id, result.Invoice.InvoiceNumber);
    }
}
