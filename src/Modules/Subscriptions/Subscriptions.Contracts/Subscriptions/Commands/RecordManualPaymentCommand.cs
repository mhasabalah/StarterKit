namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// Admin records money received offline (cash, bank transfer seen on the statement, ...) against the payment the
/// subscription currently owes: the activation payment (PendingApproval), the renewal payment (PastDue) or an upgrade
/// proration payment (Active). Partial amounts accumulate (PartiallyPaid); when the cumulative amount reaches the
/// amount due, the subscription activates / renews exactly as on a proof approval (invoice + ledger in the same
/// transaction). Overpayment is rejected (400) unless <c>Subscriptions:ManualPayments:AllowOverpaymentCredit</c>.
/// </summary>
/// <param name="Amount">Gross amount received, &gt; 0, at most 2 decimals, in the payment's currency.</param>
/// <param name="Method">How the money arrived (free text, e.g. <c>Cash</c>, <c>BankTransfer</c>).</param>
/// <param name="Reference">Bank / receipt reference (optional).</param>
/// <param name="ReceivedAt">When the money was received (defaults to now; not in the future).</param>
/// <param name="PaymentId">The owed payment; optional when exactly one payment is owed.</param>
/// <param name="Version">The subscription version the admin saw (stale = 409).</param>
public record RecordManualPaymentCommand(
    Guid SubscriptionId,
    Guid AdminId,
    decimal Amount,
    string Method,
    string? Reference = null,
    DateTimeOffset? ReceivedAt = null,
    string? Note = null,
    Guid? PaymentId = null,
    long? Version = null) : ICommand<RecordManualPaymentResult>;

/// <param name="Completed">True when this receipt paid the amount due in full (subscription activated/renewed).</param>
public record RecordManualPaymentResult(
    Guid SubscriptionId,
    Guid PaymentId,
    Guid ReceiptId,
    string PaymentStatus,
    decimal AmountDue,
    decimal AmountReceived,
    decimal OutstandingAmount,
    string Currency,
    bool Completed,
    string SubscriptionStatus,
    Guid? InvoiceId,
    string? InvoiceNumber);
