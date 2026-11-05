using Subscriptions.Contracts.Subscriptions.Events;

namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>
/// One payment owed by a subscriber (activation, renewal or upgrade proration). <see cref="Amount"/> is gross.
/// Lifecycle: Pending → Submitted (proof uploaded) → Paid | Rejected (→ Submitted again); Pending/Rejected/Submitted → Failed.
/// Manual (offline) receipts: Pending/Submitted/Rejected/PartiallyPaid → PartiallyPaid (still owed) | Paid (fully received).
/// <see cref="AmountReceived"/> accumulates every received amount (manual receipts and the approved proof).
/// </summary>
public class SubscriptionPayment : Aggregate<Guid>
{
    /// <summary>Payment method type stored for complimentary (zero-price) payments.</summary>
    public const string ComplimentaryMethod = "Complimentary";

    /// <summary>Payment method type stored for admin-recorded offline payments.</summary>
    public const string ManualMethod = "Manual";

    private readonly List<SubscriptionPaymentReceipt> _receipts = [];

    public Guid SubscriptionId { get; private set; }
    public PaymentPurpose Purpose { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Renewal payments: deferred upgrade proration included in <see cref="Amount"/> (tax basis).</summary>
    public decimal IncludedProrationAmount { get; private set; }

    /// <summary>Coupon redeemed on this bill (snapshot); null = none.</summary>
    public string? CouponCode { get; private set; }

    /// <summary>Coupon discount already taken off this bill's price (tax basis, before tax). 0 = none.</summary>
    public decimal CouponDiscountAmount { get; private set; }

    /// <summary>Proration payments: the plan before the upgrade (used to revert an unpaid upgrade).</summary>
    public Guid? OriginalPlanId { get; private set; }

    public string Currency { get; private set; } = default!;
    public PaymentStatus Status { get; private set; }
    public Guid? PaymentMethodId { get; private set; }
    public string? PaymentMethodType { get; private set; }
    public string? PaymentMethodDisplayName { get; private set; }
    public string? PaymentMethodValue { get; private set; }
    public string? PaymentMethodDescription { get; private set; }
    public Guid? ProofAttachmentId { get; private set; }

    /// <summary>Lowercase hex SHA-256 of the latest proof file (from the Attachment module), or null.</summary>
    public string? ProofContentHash { get; private set; }

    /// <summary>
    /// True when the latest proof has the same content hash as another payment's proof. A warning for the reviewer;
    /// it never blocks the flow.
    /// </summary>
    public bool DuplicateProof { get; private set; }

    /// <summary>The payment whose proof had the same content (when <see cref="DuplicateProof"/>).</summary>
    public Guid? DuplicateOfPaymentId { get; private set; }

    /// <summary>Gross amount received so far (manual receipts + approved proof). Equals <see cref="Amount"/> once Paid.</summary>
    public decimal AmountReceived { get; private set; }

    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? Notes { get; private set; }
    public new DateTimeOffset CreatedAt { get; private set; }

    public Subscription Subscription { get; private set; } = null!;

    /// <summary>Manual receipts recorded against this payment (insert-only).</summary>
    public IReadOnlyList<SubscriptionPaymentReceipt> Receipts => _receipts.AsReadOnly();

    /// <summary>Gross amount still owed (never negative).</summary>
    public decimal OutstandingAmount => Math.Max(0m, Amount - AmountReceived);

    /// <summary>
    /// Still owed: not paid, not failed (Pending, Submitted, Rejected awaiting a new proof, or PartiallyPaid).
    /// </summary>
    public bool IsAwaitingPayment => Status is PaymentStatus.Pending or PaymentStatus.Submitted or PaymentStatus.Rejected
        or PaymentStatus.PartiallyPaid;

    /// <summary>Statuses from which a subscriber may upload a proof.</summary>
    public bool AcceptsProof => Status is PaymentStatus.Pending or PaymentStatus.Rejected or PaymentStatus.PartiallyPaid;

    private SubscriptionPayment()
    {
    }

    public static SubscriptionPayment Create(
        Guid subscriptionId,
        PaymentPurpose purpose,
        decimal amount,
        string currency,
        DateTimeOffset now,
        decimal includedProrationAmount = 0m,
        Guid? originalPlanId = null)
    {
        if (amount < 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Payment amount cannot be negative.");

        return new SubscriptionPayment
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            Purpose = purpose,
            Amount = amount,
            IncludedProrationAmount = includedProrationAmount,
            OriginalPlanId = originalPlanId,
            Currency = currency,
            Status = PaymentStatus.Pending,
            CreatedAt = now
        };
    }

    /// <summary>Records the coupon behind this bill's amount (snapshot for the invoice).</summary>
    public void RecordCoupon(string code, decimal discountAmount)
    {
        if (discountAmount <= 0m)
            return;

        CouponCode = code;
        CouponDiscountAmount = discountAmount;
    }

    /// <summary>A zero-amount, already paid activation payment for a complimentary (comp) activation.</summary>
    public static SubscriptionPayment CreateComplimentary(Guid subscriptionId, string currency, Guid adminId, DateTimeOffset now)
    {
        var payment = Create(subscriptionId, PaymentPurpose.Activation, 0m, currency, now);
        payment.PaymentMethodType = ComplimentaryMethod;
        payment.PaymentMethodDisplayName = ComplimentaryMethod;
        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = now;
        payment.ReviewedBy = adminId;
        payment.ReviewedAt = now;
        payment.Notes = "Complimentary activation";
        return payment;
    }

    public void SubmitProof(
        Guid attachmentId,
        PaymentMethodSnapshot paymentMethod,
        Guid subscriberId,
        DateTimeOffset now,
        string? proofContentHash = null,
        Guid? duplicateOfPaymentId = null)
    {
        if (!AcceptsProof)
        {
            throw new ConflictException($"Cannot transition payment from {Status} to {PaymentStatus.Submitted}.");
        }

        ProofAttachmentId = attachmentId;
        ProofContentHash = proofContentHash;
        DuplicateOfPaymentId = duplicateOfPaymentId;
        DuplicateProof = duplicateOfPaymentId is not null;
        PaymentMethodId = paymentMethod.Id;
        PaymentMethodType = paymentMethod.Type;
        PaymentMethodDisplayName = paymentMethod.DisplayName;
        PaymentMethodValue = paymentMethod.Value;
        PaymentMethodDescription = paymentMethod.Description;
        Status = PaymentStatus.Submitted;
        SubmittedAt = now;
        ReviewedBy = null;
        ReviewedAt = null;
        Notes = null;
        AddDomainEvent(new PaymentSubmittedEvent(Id, SubscriptionId, subscriberId));
    }

    /// <summary>
    /// Approves the submitted proof: the payment is Paid in full. Returns the amount received by THIS approval
    /// (the outstanding amount; less than <see cref="Amount"/> when manual receipts were recorded before).
    /// </summary>
    public decimal Approve(Guid adminId, Guid subscriberId, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Submitted)
        {
            throw new ConflictException($"Cannot transition payment from {Status} to {PaymentStatus.Paid}.");
        }

        var received = OutstandingAmount;
        MarkPaid(adminId, subscriberId, now);
        return received;
    }

    /// <summary>
    /// Records money received offline (cash, a transfer seen on the bank statement, ...). Partial amounts accumulate
    /// (<see cref="PaymentStatus.PartiallyPaid"/>); when the cumulative amount reaches <see cref="Amount"/> the
    /// payment is Paid. Receiving more than is owed is refused (400) unless <paramref name="allowOverpayment"/>.
    /// </summary>
    /// <returns>The receipt and whether this receipt completed the payment.</returns>
    public (SubscriptionPaymentReceipt Receipt, bool Completed) RecordReceipt(
        decimal amount,
        string method,
        string? reference,
        DateTimeOffset receivedAt,
        string? note,
        Guid adminId,
        Guid subscriberId,
        DateTimeOffset now,
        bool allowOverpayment = false)
    {
        if (!IsAwaitingPayment)
            throw new ConflictException($"Payment is {Status}; no amount is outstanding.");

        if (amount <= 0m || decimal.Round(amount, 2) != amount)
            throw new BadRequestException("Amount must be positive with at most 2 decimals.");

        if (string.IsNullOrWhiteSpace(method))
            throw new BadRequestException("A payment method is required.");

        if (amount > OutstandingAmount && !allowOverpayment)
            throw new BadRequestException(
                $"Amount {amount:F2} exceeds the outstanding {OutstandingAmount:F2} {Currency}; overpayment is not allowed.");

        var receipt = SubscriptionPaymentReceipt.Create(
            Id, SubscriptionId, amount, Currency, method, reference, receivedAt, note, adminId, now);
        _receipts.Add(receipt);

        AmountReceived += amount;
        PaymentMethodType ??= ManualMethod;
        PaymentMethodDisplayName ??= receipt.Method;

        if (AmountReceived >= Amount)
        {
            MarkPaid(adminId, subscriberId, now);
            return (receipt, true);
        }

        Status = PaymentStatus.PartiallyPaid;
        ReviewedBy = adminId;
        ReviewedAt = now;
        return (receipt, false);
    }

    public void Reject(Guid adminId, string reason, Guid subscriberId, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Submitted)
        {
            throw new ConflictException($"Cannot transition payment from {Status} to {PaymentStatus.Rejected}.");
        }

        // A partially paid payment keeps the money already received: back to PartiallyPaid (a new proof can follow).
        Status = AmountReceived > 0m ? PaymentStatus.PartiallyPaid : PaymentStatus.Rejected;
        ReviewedBy = adminId;
        ReviewedAt = now;
        Notes = reason;
        AddDomainEvent(new PaymentRejectedEvent(Id, SubscriptionId, subscriberId, reason));
    }

    /// <summary>Closes an unpaid payment (expiry, upgrade revert, comp waiver). No-op once Paid or Failed.</summary>
    public void MarkFailed(string? note = null)
    {
        if (!IsAwaitingPayment)
        {
            return;
        }

        Status = PaymentStatus.Failed;
        if (note is not null)
        {
            Notes = note;
        }
    }

    private void MarkPaid(Guid adminId, Guid subscriberId, DateTimeOffset now)
    {
        AmountReceived = Math.Max(AmountReceived, Amount);
        Status = PaymentStatus.Paid;
        PaidAt = now;
        ReviewedBy = adminId;
        ReviewedAt = now;
        AddDomainEvent(new PaymentApprovedEvent(Id, SubscriptionId, subscriberId));
    }
}
