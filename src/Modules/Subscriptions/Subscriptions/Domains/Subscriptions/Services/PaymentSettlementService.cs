namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>Outcome of settling a fully paid payment.</summary>
/// <param name="InvoiceIssued">True when the invoice was issued now (deliver it after commit).</param>
public sealed record SettlementResult(
    SubscriptionInvoice Invoice,
    bool InvoiceIssued,
    SubscriptionStatus FromStatus,
    SubscriptionStatus ToStatus);

/// <summary>
/// THE completion path of a payment, shared by proof approvals (activation, renewal, proration) and manual /
/// partial payments, so money is never booked two different ways:
/// <list type="number">
/// <item><see cref="EnsureSettleable"/> before touching the payment (status for its purpose, currency);</item>
/// <item>the caller marks the payment Paid (<c>Approve</c> or the completing <c>RecordReceipt</c>);</item>
/// <item><see cref="SettleAsync"/>: activation → Active / renewal → Active (next period) + transition log, subscriber
/// snapshot refresh, invoice issued (or the existing proration invoice found), ledger Payment entry for the amount
/// received by this step - all in the caller's transaction;</item>
/// <item>after commit: <see cref="AfterCommitAsync"/> (cache eviction + invoice delivery).</item>
/// </list>
/// </summary>
public interface IPaymentSettlementService
{
    void EnsureSettleable(Subscription subscription, SubscriptionPayment payment);

    Task<SettlementResult> SettleAsync(
        Subscription subscription,
        SubscriptionPayment payment,
        decimal amountReceived,
        Guid adminId,
        DateTimeOffset now,
        string? paymentMethod = null,
        string? reference = null,
        CancellationToken ct = default);

    /// <summary>Ledger entry for money received that does not (yet) complete the payment.</summary>
    void RecordPartialReceipt(
        Subscription subscription,
        SubscriptionPayment payment,
        decimal amountReceived,
        Guid adminId,
        DateTimeOffset now,
        string? paymentMethod,
        string? reference);

    Task AfterCommitAsync(Subscription subscription, SettlementResult? result, CancellationToken ct = default);
}

public sealed class PaymentSettlementService(
    SubscriptionsDbContext dbContext,
    IInvoiceIssuer invoiceIssuer,
    ILedgerRepository ledger,
    ISubscriberInfoProvider subscriberInfoProvider,
    IDistributedCache cache)
    : IPaymentSettlementService
{
    public void EnsureSettleable(Subscription subscription, SubscriptionPayment payment)
    {
        if (payment.SubscriptionId != subscription.Id)
            throw new InvalidOperationException("The payment does not belong to the subscription.");

        if (!string.Equals(payment.Currency, subscription.Currency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentAmountMismatchException(
                subscription.Id, payment.Amount, subscription.Currency, payment.Amount, payment.Currency);

        var required = RequiredStatus(payment.Purpose);
        if (subscription.Status != required)
            throw new ConflictException(
                $"A {payment.Purpose} payment can only be settled while the subscription is {required} (it is {subscription.Status}).");
    }

    public async Task<SettlementResult> SettleAsync(
        Subscription subscription,
        SubscriptionPayment payment,
        decimal amountReceived,
        Guid adminId,
        DateTimeOffset now,
        string? paymentMethod = null,
        string? reference = null,
        CancellationToken ct = default)
    {
        if (payment.Status != PaymentStatus.Paid)
            throw new InvalidOperationException("Only a paid payment can be settled.");

        var from = subscription.Status;
        switch (payment.Purpose)
        {
            case PaymentPurpose.Activation:
                subscription.Activate(now);
                break;
            case PaymentPurpose.Renewal:
                subscription.Renew(now);
                break;
            case PaymentPurpose.Proration:
                if (subscription.Status != SubscriptionStatus.Active)
                    throw new ConflictException("Proration payments can only be settled for active subscriptions.");
                break;
            default:
                throw new InvalidOperationException($"Unsupported payment purpose {payment.Purpose}.");
        }

        if (from != subscription.Status)
        {
            dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
                subscription.Id, subscription.SubscriberId, from, subscription.Status, adminId, "Admin", null));
            await RefreshSubscriberSnapshotAsync(subscriberInfoProvider, subscription, ct);
        }

        // The proration invoice is normally issued with the upgrade; issue it only if it is missing (legacy rows).
        var invoice = payment.Purpose == PaymentPurpose.Proration
            ? await dbContext.Invoices.FirstOrDefaultAsync(
                x => x.PaymentId == payment.Id && x.Type == InvoiceType.Proration, ct)
            : null;
        var issued = invoice is null;
        invoice ??= await invoiceIssuer.IssueForPaymentAsync(subscription, payment, ct);

        if (amountReceived > 0m)
        {
            ledger.Add(LedgerEntry.ForPayment(
                subscription, payment, amountReceived, invoice.Id, adminId, now, paymentMethod, reference));
        }

        return new SettlementResult(invoice, issued, from, subscription.Status);
    }

    public void RecordPartialReceipt(
        Subscription subscription,
        SubscriptionPayment payment,
        decimal amountReceived,
        Guid adminId,
        DateTimeOffset now,
        string? paymentMethod,
        string? reference) =>
        ledger.Add(LedgerEntry.ForPayment(
            subscription, payment, amountReceived, null, adminId, now, paymentMethod, reference,
            $"{payment.Purpose} partial payment received"));

    public async Task AfterCommitAsync(Subscription subscription, SettlementResult? result, CancellationToken ct = default)
    {
        await cache.RemoveAsync(SubscriptionCacheKeys.ForSubscriber(subscription.SubscriberId), ct);

        if (result is { InvoiceIssued: true })
        {
            invoiceIssuer.EnqueueDelivery(result.Invoice.Id);
        }
    }

    /// <summary>Copies the subscriber's current name/email onto the subscription (admin search columns).</summary>
    public static async Task RefreshSubscriberSnapshotAsync(
        ISubscriberInfoProvider provider,
        Subscription subscription,
        CancellationToken ct)
    {
        var info = await provider.GetAsync(subscription.SubscriberId, ct);
        if (info is not null)
        {
            subscription.UpdateSubscriberSnapshot(info.DisplayName, info.Email);
        }
    }

    private static SubscriptionStatus RequiredStatus(PaymentPurpose purpose) => purpose switch
    {
        PaymentPurpose.Activation => SubscriptionStatus.PendingApproval,
        PaymentPurpose.Renewal => SubscriptionStatus.PastDue,
        PaymentPurpose.Proration => SubscriptionStatus.Active,
        _ => throw new InvalidOperationException($"Unsupported payment purpose {purpose}.")
    };
}
