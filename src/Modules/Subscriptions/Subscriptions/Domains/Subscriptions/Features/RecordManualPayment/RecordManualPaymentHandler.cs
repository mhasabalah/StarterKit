using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Features.RecordManualPayment;

public class RecordManualPaymentCommandValidator : AbstractValidator<RecordManualPaymentCommand>
{
    public RecordManualPaymentCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.SubscriptionId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0m).PrecisionScale(18, 2, true);
        RuleFor(x => x.Method).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.Note).MaximumLength(1000);
        RuleFor(x => x.ReceivedAt)
            .Must(x => x is null || x.Value <= timeProvider.GetUtcNow().AddMinutes(5))
            .WithMessage("ReceivedAt cannot be in the future.");
    }
}

/// <summary>
/// Records an offline (manual) payment, possibly partial. A partial receipt books a ledger Payment entry and leaves
/// the payment PartiallyPaid; the receipt that completes the amount due settles it through the SAME path as a proof
/// approval (<see cref="IPaymentSettlementService"/>: activate / renew, invoice, ledger). One transaction.
/// </summary>
public class RecordManualPaymentHandler(
    ISubscriptionRepository subscriptionRepository,
    IPaymentSettlementService settlement,
    IAdminAuditWriter audit,
    SubscriptionsDbContext dbContext,
    IOptions<SubscriptionOptions> options,
    TimeProvider timeProvider)
    : ICommandHandler<RecordManualPaymentCommand, RecordManualPaymentResult>
{
    public async Task<RecordManualPaymentResult> Handle(
        RecordManualPaymentCommand command,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var subscription = await subscriptionRepository.GetByIdAsync(command.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(command.SubscriptionId);

        dbContext.ExpectVersion(subscription, command.Version);

        if (subscription.SubscriberId == command.AdminId)
            throw new SelfApprovalNotAllowedException();

        var payment = await PaymentSelection.ResolveOwedAsync(
            subscriptionRepository, subscription, command.PaymentId, cancellationToken);
        settlement.EnsureSettleable(subscription, payment);

        var now = timeProvider.GetUtcNow();
        var subscriptionStatusBefore = subscription.Status;
        var paymentStatusBefore = payment.Status;
        var receivedBefore = payment.AmountReceived;

        var (receipt, completed) = payment.RecordReceipt(
            command.Amount,
            command.Method,
            command.Reference,
            command.ReceivedAt ?? now,
            command.Note,
            command.AdminId,
            subscription.SubscriberId,
            now,
            options.Value.ManualPayments.AllowOverpaymentCredit);

        SettlementResult? result = null;
        if (completed)
        {
            result = await settlement.SettleAsync(
                subscription, payment, command.Amount, command.AdminId, now, receipt.Method, receipt.Reference,
                cancellationToken);
        }
        else
        {
            settlement.RecordPartialReceipt(
                subscription, payment, command.Amount, command.AdminId, now, receipt.Method, receipt.Reference);
        }

        // Always update the subscription row: the version check applies and concurrent receipts serialize.
        subscription.Touch(now);

        audit.Record(
            "RecordManualPayment",
            payment.Id,
            "Payment",
            subscription.SubscriberId,
            command.Note ?? command.Reference,
            command.Amount,
            payment.Currency,
            before: new
            {
                SubscriptionStatus = subscriptionStatusBefore.ToString(),
                PaymentStatus = paymentStatusBefore.ToString(),
                AmountReceived = receivedBefore
            },
            after: new
            {
                SubscriptionStatus = subscription.Status.ToString(),
                PaymentStatus = payment.Status.ToString(),
                payment.AmountReceived,
                payment.OutstandingAmount,
                ReceiptId = receipt.Id,
                receipt.Method,
                receipt.Reference,
                receipt.ReceivedAt,
                InvoiceId = result?.Invoice.Id,
                result?.Invoice.InvoiceNumber
            },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await settlement.AfterCommitAsync(subscription, result, cancellationToken);

        return new RecordManualPaymentResult(
            subscription.Id,
            payment.Id,
            receipt.Id,
            payment.Status.ToString(),
            payment.Amount,
            payment.AmountReceived,
            payment.OutstandingAmount,
            payment.Currency,
            completed,
            subscription.Status.ToString(),
            result?.Invoice.Id,
            result?.Invoice.InvoiceNumber);
    }
}
