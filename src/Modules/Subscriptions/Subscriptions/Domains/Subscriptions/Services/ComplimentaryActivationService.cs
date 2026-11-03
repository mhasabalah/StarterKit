using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Complimentary (zero-price) activation shared by the comp and reactivate-as-comp admin actions: the subscription
/// becomes Active for N months, every payment still owed is waived (Failed), a Paid 0-amount activation payment gets a
/// 0-total invoice and a ledger Adjustment of 0, and the transition is logged. Runs in the caller's transaction.
/// </summary>
public interface IComplimentaryActivationService
{
    Task<ComplimentaryActivation> ActivateAsync(
        Subscription subscription,
        PlanSnapshot plan,
        int months,
        Guid adminId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct = default);
}

public sealed record ComplimentaryActivation(
    SubscriptionPayment Payment,
    SubscriptionInvoice Invoice,
    SubscriptionStatus FromStatus,
    IReadOnlyList<Guid> WaivedPaymentIds);

public sealed class ComplimentaryActivationService(
    SubscriptionsDbContext dbContext,
    ISubscriptionRepository subscriptionRepository,
    IInvoiceIssuer invoiceIssuer,
    ILedgerRepository ledger,
    ISubscriberInfoProvider subscriberInfoProvider,
    IOptions<SubscriptionOptions> options)
    : IComplimentaryActivationService
{
    public async Task<ComplimentaryActivation> ActivateAsync(
        Subscription subscription,
        PlanSnapshot plan,
        int months,
        Guid adminId,
        string reason,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var from = subscription.Status;
        var payment = subscription.ActivateComplimentary(plan, months, adminId, options.Value.ReactivationWindowDays, now);

        var waived = new List<Guid>();
        foreach (var owed in await subscriptionRepository.GetAwaitingPaymentsAsync(subscription.Id, ct))
        {
            if (owed.Id == payment.Id)
                continue;

            owed.MarkFailed("Waived: complimentary activation");
            waived.Add(owed.Id);
        }

        dbContext.TransitionLogs.Add(SubscriptionTransitionLog.Create(
            subscription.Id,
            subscription.SubscriberId,
            from,
            subscription.Status,
            adminId,
            "Admin",
            $"Complimentary {months} month(s): {reason}"));

        await PaymentSettlementService.RefreshSubscriberSnapshotAsync(subscriberInfoProvider, subscription, ct);

        var invoice = await invoiceIssuer.IssueForPaymentAsync(subscription, payment, ct);
        ledger.Add(LedgerEntry.Adjustment(
            subscription,
            0m,
            $"Complimentary activation ({months} month(s)): {reason}",
            adminId,
            now,
            payment.Id,
            invoice.Id,
            SubscriptionPayment.ComplimentaryMethod));

        return new ComplimentaryActivation(payment, invoice, from, waived);
    }
}
