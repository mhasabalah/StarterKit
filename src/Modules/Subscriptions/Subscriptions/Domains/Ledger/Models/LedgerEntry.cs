using Subscriptions.Contracts.Ledger.Enums;

namespace Subscriptions.Domains.Ledger.Models;

/// <summary>
/// One immutable money movement: the single source for billing KPIs (dashboard) and exports.
/// <list type="bullet">
/// <item><b>Insert-only.</b> No setters, no mutators; <c>LedgerEntryConfiguration</c> makes every property throw when
/// modified after save, <c>SubscriptionsDbContext</c> refuses Modified/Deleted ledger rows, and
/// <c>ILedgerRepository</c> only adds. Mistakes are corrected with a new compensating entry.</item>
/// <item><b>Same transaction as the money event</b> (approval, manual receipt, refund processed, credit note,
/// comp): the entry is added to the same DbContext before the one SaveChanges, so both commit or neither does.</item>
/// <item><b>Signed amounts</b> (gross, decimal(18,2)): Payment &gt; 0, Refund &lt; 0, CreditNote &lt; 0,
/// Adjustment any sign (0 for a comp). Sum of a currency's entries = net money for that currency.</item>
/// <item>Plan and payment method are snapshotted so revenue can be grouped without joining live data.</item>
/// </list>
/// </summary>
public class LedgerEntry
{
    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public LedgerEntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public Guid SubscriptionId { get; private set; }
    public Guid SubscriberId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public Guid? RefundId { get; private set; }
    public Guid ActorId { get; private set; }
    public string Description { get; private set; } = default!;
    public Guid? PlanId { get; private set; }
    public string? PlanName { get; private set; }

    /// <summary>Payment method type/name (e.g. <c>BankTransfer</c>, a manual method, <c>Complimentary</c>), or null.</summary>
    public string? PaymentMethod { get; private set; }

    /// <summary>External reference (bank reference, refund transaction ref, receipt reference), or null.</summary>
    public string? Reference { get; private set; }

    private LedgerEntry()
    {
    }

    /// <summary>Money received for <paramref name="payment"/>: <paramref name="amountReceived"/> (&gt; 0) of it.</summary>
    public static LedgerEntry ForPayment(
        Subscription subscription,
        SubscriptionPayment payment,
        decimal amountReceived,
        Guid? invoiceId,
        Guid actorId,
        DateTimeOffset occurredAt,
        string? paymentMethod = null,
        string? reference = null,
        string? description = null)
    {
        if (amountReceived <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amountReceived), amountReceived, "A payment entry must be positive.");
        EnsureSame(subscription, payment);

        return New(
            LedgerEntryType.Payment,
            amountReceived,
            payment.Currency,
            subscription,
            actorId,
            occurredAt,
            description ?? $"{payment.Purpose} payment received",
            paymentMethod ?? payment.PaymentMethodType,
            reference,
            payment.Id,
            invoiceId);
    }

    /// <summary>A refund paid out: amount is the NEGATED refunded amount.</summary>
    public static LedgerEntry ForRefund(
        SubscriptionRefundRequest refund,
        Guid? invoiceId,
        Guid actorId,
        DateTimeOffset occurredAt,
        decimal? amount = null)
    {
        // amount < RequestedAmount: the provider's own webhook already booked the rest of this refund.
        var refunded = amount ?? refund.RequestedAmount;
        if (refunded <= 0m)
            throw new ArgumentOutOfRangeException(nameof(refund), refunded, "A refund must be positive.");

        return new LedgerEntry
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            Type = LedgerEntryType.Refund,
            Amount = -refunded,
            Currency = refund.Currency,
            SubscriptionId = refund.SubscriptionId,
            SubscriberId = refund.SubscriberId,
            RefundId = refund.Id,
            InvoiceId = invoiceId,
            ActorId = actorId,
            Description = Truncate($"Refund ({refund.RefundType})"),
            PlanId = refund.PlanId,
            PlanName = refund.PlanName,
            Reference = Truncate(refund.ExternalTransactionRef, 200)
        };
    }

    /// <summary>
    /// A refund the payment provider reported on its own (dashboard refund): no refund request exists.
    /// <paramref name="providerRefundId"/> is the reference. Amount is the NEGATED refunded amount.
    /// </summary>
    public static LedgerEntry ForProviderRefund(
        Subscription subscription,
        Guid paymentId,
        decimal amount,
        string currency,
        Guid? invoiceId,
        Guid actorId,
        DateTimeOffset occurredAt,
        string provider,
        string providerRefundId)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A refund must be positive.");

        return New(
            LedgerEntryType.Refund,
            -amount,
            currency,
            subscription,
            actorId,
            occurredAt,
            $"Refund reported by {provider}",
            provider,
            providerRefundId,
            paymentId,
            invoiceId);
    }

    /// <summary>A standalone credit note (negative amounts) issued against an invoice.</summary>
    public static LedgerEntry ForCreditNote(SubscriptionInvoice creditNote, Guid actorId, DateTimeOffset occurredAt)
    {
        if (!creditNote.IsCreditNote || creditNote.TotalAmount >= 0m)
            throw new ArgumentException("Expected a credit note with a negative total.", nameof(creditNote));

        return new LedgerEntry
        {
            Id = Guid.NewGuid(),
            OccurredAt = occurredAt,
            Type = LedgerEntryType.CreditNote,
            Amount = creditNote.TotalAmount,
            Currency = creditNote.Currency,
            SubscriptionId = creditNote.SubscriptionId,
            SubscriberId = creditNote.SubscriberId,
            PaymentId = creditNote.PaymentId,
            InvoiceId = creditNote.Id,
            ActorId = actorId,
            Description = Truncate($"Credit note {creditNote.InvoiceNumber}"),
            PlanId = creditNote.PlanId,
            PlanName = creditNote.PlanName,
            Reference = creditNote.InvoiceNumber
        };
    }

    /// <summary>A manual adjustment (any sign), e.g. a complimentary activation recorded with amount 0.</summary>
    public static LedgerEntry Adjustment(
        Subscription subscription,
        decimal amount,
        string description,
        Guid actorId,
        DateTimeOffset occurredAt,
        Guid? paymentId = null,
        Guid? invoiceId = null,
        string? paymentMethod = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("An adjustment needs a description.", nameof(description));

        return New(
            LedgerEntryType.Adjustment,
            decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            subscription.Currency,
            subscription,
            actorId,
            occurredAt,
            description,
            paymentMethod,
            null,
            paymentId,
            invoiceId);
    }

    private static LedgerEntry New(
        LedgerEntryType type,
        decimal amount,
        string currency,
        Subscription subscription,
        Guid actorId,
        DateTimeOffset occurredAt,
        string description,
        string? paymentMethod,
        string? reference,
        Guid? paymentId,
        Guid? invoiceId) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAt = occurredAt,
        Type = type,
        Amount = amount,
        Currency = currency,
        SubscriptionId = subscription.Id,
        SubscriberId = subscription.SubscriberId,
        PaymentId = paymentId,
        InvoiceId = invoiceId,
        ActorId = actorId,
        Description = Truncate(description)!,
        PlanId = subscription.PlanId,
        PlanName = subscription.PlanName,
        PaymentMethod = Truncate(paymentMethod, 100),
        Reference = Truncate(reference, 200)
    };

    private static void EnsureSame(Subscription subscription, SubscriptionPayment payment)
    {
        if (payment.SubscriptionId != subscription.Id)
            throw new InvalidOperationException("The payment does not belong to the subscription.");
    }

    private static string? Truncate(string? value, int max = 500) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
