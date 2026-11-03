using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.ApproveRenewal;

public class ApproveRenewalCommandValidator : AbstractValidator<ApproveRenewalCommand>
{
    public ApproveRenewalCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
    }
}

/// <summary>
/// Approves a renewal payment. The amount owed is the amount on the renewal PAYMENT (priced at the boundary by the
/// renewal orchestrator, incl. deferred proration), never the live plan price. The new period starts at the previous
/// renewal date. Invoice and ledger entry are inserted in the same transaction (<see cref="IPaymentSettlementService"/>).
/// </summary>
public class ApproveRenewalHandler(
    ISubscriptionRepository subscriptionRepository,
    IPaymentSettlementService settlement,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<ApproveRenewalCommand, ApproveRenewalResult>
{
    public async Task<ApproveRenewalResult> Handle(
        ApproveRenewalCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        dbContext.ExpectVersion(subscription, command.Version);

        if (subscription.Status != SubscriptionStatus.PastDue)
            throw new ConflictException("Subscription is not past due.");

        if (subscription.SubscriberId == command.AdminId)
            throw new SelfApprovalNotAllowedException();

        var payment = await PaymentSelection.ResolveSubmittedAsync(
            subscriptionRepository, subscription.Id, command.PaymentId, PaymentPurpose.Renewal, cancellationToken);
        settlement.EnsureSettleable(subscription, payment);

        var previousRenewal = subscription.RenewalDate;
        var now = DateTimeOffset.UtcNow;
        var received = payment.Approve(command.AdminId, subscription.SubscriberId, now);
        var result = await settlement.SettleAsync(
            subscription, payment, received, command.AdminId, now, ct: cancellationToken);

        audit.Record(
            "ApproveRenewal",
            subscription.Id,
            "Subscription",
            subscription.SubscriberId,
            amount: received,
            currency: payment.Currency,
            before: new { Status = result.FromStatus.ToString(), RenewalDate = previousRenewal },
            after: new
            {
                Status = subscription.Status.ToString(),
                subscription.RenewalDate,
                PaymentId = payment.Id,
                InvoiceId = result.Invoice.Id,
                result.Invoice.InvoiceNumber,
                payment.DuplicateProof,
                payment.DuplicateOfPaymentId
            },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await settlement.AfterCommitAsync(subscription, result, cancellationToken);

        return new ApproveRenewalResult(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.RenewalDate!.Value,
            payment.Id,
            result.Invoice.Id,
            result.Invoice.InvoiceNumber);
    }
}
