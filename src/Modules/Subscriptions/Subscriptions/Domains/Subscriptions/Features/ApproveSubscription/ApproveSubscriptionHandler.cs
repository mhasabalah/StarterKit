using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.ApproveSubscription;

public class ApproveSubscriptionCommandValidator : AbstractValidator<ApproveSubscriptionCommand>
{
    public ApproveSubscriptionCommandValidator()
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
    }
}

/// <summary>
/// Approves the activation payment and activates the subscription. Invoice row and ledger entry are inserted in the
/// SAME transaction (<see cref="IPaymentSettlementService"/>); PDF + email follow in a background job. It never
/// touches another subscription: the one-open-subscription index guarantees there is none.
/// </summary>
public class ApproveSubscriptionHandler(
    ISubscriptionRepository subscriptionRepository,
    IPaymentSettlementService settlement,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext)
    : ICommandHandler<ApproveSubscriptionCommand, ApproveSubscriptionResult>
{
    public async Task<ApproveSubscriptionResult> Handle(
        ApproveSubscriptionCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        dbContext.ExpectVersion(subscription, command.Version);

        if (subscription.Status != SubscriptionStatus.PendingApproval)
            throw new ConflictException("Subscription is not pending approval.");

        if (subscription.SubscriberId == command.AdminId)
            throw new SelfApprovalNotAllowedException();

        var payment = await PaymentSelection.ResolveSubmittedAsync(
            subscriptionRepository, subscription.Id, command.PaymentId, PaymentPurpose.Activation, cancellationToken);
        settlement.EnsureSettleable(subscription, payment);

        var now = DateTimeOffset.UtcNow;
        var received = payment.Approve(command.AdminId, subscription.SubscriberId, now);
        var result = await settlement.SettleAsync(
            subscription, payment, received, command.AdminId, now, ct: cancellationToken);

        audit.Record(
            "ApproveSubscription",
            subscription.Id,
            "Subscription",
            subscription.SubscriberId,
            amount: received,
            currency: payment.Currency,
            before: new { Status = result.FromStatus.ToString(), PaymentStatus = PaymentStatus.Submitted.ToString() },
            after: new
            {
                Status = subscription.Status.ToString(),
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

        return new ApproveSubscriptionResult(
            subscription.Id,
            subscription.Status.ToString(),
            subscription.StartDate!.Value,
            subscription.RenewalDate!.Value,
            payment.Id,
            result.Invoice.Id,
            result.Invoice.InvoiceNumber);
    }
}
