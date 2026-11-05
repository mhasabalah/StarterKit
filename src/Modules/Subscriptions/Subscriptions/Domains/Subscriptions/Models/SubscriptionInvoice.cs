using Subscriptions.Domains.Subscriptions.Billing;

namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>A party printed on an invoice, snapshotted at issue time.</summary>
public sealed record InvoiceParty(string Name, string? Email, string? Address, string? TaxId);

/// <summary>Everything needed to issue an invoice; the number is allocated by the issuer.</summary>
public sealed record InvoiceDraft(
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid? PaymentId,
    InvoiceType Type,
    string Description,
    Guid PlanId,
    string PlanName,
    int BillingCycleInMonths,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    InvoiceParty Buyer,
    InvoiceParty Seller,
    TaxBreakdown Amounts,
    string Currency,
    string? CouponCode = null,
    decimal CouponDiscountAmount = 0m);

/// <summary>
/// An issued invoice or credit note. The DOCUMENT (number, parties, plan, period, amounts, tax) is a snapshot taken
/// at issue time and is immutable: there are no mutators for it, and corrections are made with credit notes
/// (<see cref="InvoiceType.CreditNote"/>, negative amounts, <see cref="OriginalInvoiceId"/>).
/// Only bookkeeping/delivery fields change afterwards: <see cref="CreditedAmount"/>, PDF and email status.
/// </summary>
public class SubscriptionInvoice : Aggregate<Guid>
{
    public Guid SubscriptionId { get; private set; }
    public Guid SubscriberId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? OriginalInvoiceId { get; private set; }
    public string InvoiceNumber { get; private set; } = default!;
    public InvoiceType Type { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public string Description { get; private set; } = default!;
    public string? CreditReason { get; private set; }

    public Guid PlanId { get; private set; }
    public string PlanName { get; private set; } = default!;
    public int BillingCycleInMonths { get; private set; }
    public DateTimeOffset? PeriodStart { get; private set; }
    public DateTimeOffset? PeriodEnd { get; private set; }

    public string BuyerName { get; private set; } = default!;
    public string? BuyerEmail { get; private set; }
    public string? BuyerAddress { get; private set; }
    public string? BuyerTaxId { get; private set; }
    public string SellerName { get; private set; } = default!;
    public string? SellerAddress { get; private set; }
    public string? SellerTaxId { get; private set; }

    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public decimal TaxRate { get; private set; }
    public bool PricesIncludeTax { get; private set; }
    public string Currency { get; private set; } = default!;

    /// <summary>Coupon applied to this invoice's price (snapshot, immutable); null = none.</summary>
    public string? CouponCode { get; private set; }

    /// <summary>Discount the coupon took off the price before tax (informational; the amounts above are already net of it).</summary>
    public decimal CouponDiscountAmount { get; private set; }

    /// <summary>Bookkeeping: gross amount already credited by credit notes (positive). Not part of the document.</summary>
    public decimal CreditedAmount { get; private set; }

    public InvoiceStatus Status { get; private set; }
    public string? PdfStorageKey { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public new DateTimeOffset CreatedAt { get; private set; }

    public Subscription Subscription { get; private set; } = null!;

    public bool IsCreditNote => Type == InvoiceType.CreditNote;
    public bool IsFullyCredited => !IsCreditNote && CreditedAmount >= TotalAmount;

    public TaxBreakdown Amounts => new(NetAmount, TaxAmount, TotalAmount, TaxRate, PricesIncludeTax);

    private SubscriptionInvoice()
    {
    }

    public static SubscriptionInvoice Issue(InvoiceDraft draft, string invoiceNumber, DateTimeOffset now)
    {
        if (draft.Type == InvoiceType.CreditNote)
            throw new InvalidOperationException("Use IssueCreditNote for credit notes.");

        var invoice = NewDocument(invoiceNumber, now);
        invoice.SubscriptionId = draft.SubscriptionId;
        invoice.SubscriberId = draft.SubscriberId;
        invoice.PaymentId = draft.PaymentId;
        invoice.Type = draft.Type;
        invoice.Description = Truncate(draft.Description, 500);
        invoice.PlanId = draft.PlanId;
        invoice.PlanName = draft.PlanName;
        invoice.BillingCycleInMonths = draft.BillingCycleInMonths;
        invoice.PeriodStart = draft.PeriodStart;
        invoice.PeriodEnd = draft.PeriodEnd;
        invoice.SetParties(draft.Buyer, draft.Seller);
        invoice.SetAmounts(draft.Amounts, draft.Currency);
        invoice.CouponCode = draft.CouponCode;
        invoice.CouponDiscountAmount = draft.CouponDiscountAmount;
        return invoice;
    }

    /// <summary>
    /// Issues a credit note against <paramref name="original"/> for <paramref name="amounts"/> (NEGATIVE, from
    /// <see cref="CreditNotePolicy"/>) and records the credit on the original's bookkeeping.
    /// </summary>
    public static SubscriptionInvoice IssueCreditNote(
        SubscriptionInvoice original,
        TaxBreakdown amounts,
        string invoiceNumber,
        string reason,
        DateTimeOffset now)
    {
        if (original.IsCreditNote)
            throw new ConflictException("A credit note cannot be credited.");

        if (amounts.Gross >= 0m)
            throw new InvalidOperationException("Credit note amounts must be negative.");

        var creditable = CreditNotePolicy.Creditable(original.TotalAmount, original.CreditedAmount);
        if (-amounts.Gross > creditable)
            throw new ConflictException("The credit exceeds the creditable remainder of the invoice.");

        var note = NewDocument(invoiceNumber, now);
        note.SubscriptionId = original.SubscriptionId;
        note.SubscriberId = original.SubscriberId;
        note.PaymentId = original.PaymentId;
        note.OriginalInvoiceId = original.Id;
        note.Type = InvoiceType.CreditNote;
        note.CreditReason = Truncate(reason, 500);
        note.Description = Truncate($"Credit note for invoice {original.InvoiceNumber}: {reason}", 500);
        note.PlanId = original.PlanId;
        note.PlanName = original.PlanName;
        note.BillingCycleInMonths = original.BillingCycleInMonths;
        note.PeriodStart = original.PeriodStart;
        note.PeriodEnd = original.PeriodEnd;
        note.SetParties(
            new InvoiceParty(original.BuyerName, original.BuyerEmail, original.BuyerAddress, original.BuyerTaxId),
            new InvoiceParty(original.SellerName, null, original.SellerAddress, original.SellerTaxId));
        note.SetAmounts(amounts, original.Currency);

        original.CreditedAmount += -amounts.Gross;
        return note;
    }

    public void MarkGenerated(string pdfStorageKey)
    {
        Status = InvoiceStatus.Generated;
        PdfStorageKey = pdfStorageKey;
        ErrorMessage = null;
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = InvoiceStatus.Sent;
        SentAt = now;
        ErrorMessage = null;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = InvoiceStatus.Failed;
        RetryCount++;
        ErrorMessage = Truncate(errorMessage, 500);
    }

    /// <summary>Admin retry: back to Pending so delivery runs again (PDF is kept if already generated).</summary>
    public void ResetForRetry()
    {
        Status = PdfStorageKey is null ? InvoiceStatus.Pending : InvoiceStatus.Generated;
        ErrorMessage = null;
    }

    private static SubscriptionInvoice NewDocument(string invoiceNumber, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber) || invoiceNumber.Length > InvoiceNumberFormat.MaxLength)
            throw new ArgumentException("Invalid invoice number.", nameof(invoiceNumber));

        return new SubscriptionInvoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            IssuedAt = now,
            CreatedAt = now,
            Status = InvoiceStatus.Pending,
            RetryCount = 0
        };
    }

    private void SetParties(InvoiceParty buyer, InvoiceParty seller)
    {
        BuyerName = Truncate(buyer.Name, 200);
        BuyerEmail = buyer.Email is null ? null : Truncate(buyer.Email, 256);
        BuyerAddress = buyer.Address is null ? null : Truncate(buyer.Address, 500);
        BuyerTaxId = buyer.TaxId is null ? null : Truncate(buyer.TaxId, 100);
        SellerName = Truncate(seller.Name, 200);
        SellerAddress = seller.Address is null ? null : Truncate(seller.Address, 500);
        SellerTaxId = seller.TaxId is null ? null : Truncate(seller.TaxId, 100);
    }

    private void SetAmounts(TaxBreakdown amounts, string currency)
    {
        if (amounts.Net + amounts.Tax != amounts.Gross)
            throw new InvalidOperationException("Invoice net + tax must equal the total.");

        NetAmount = amounts.Net;
        TaxAmount = amounts.Tax;
        TotalAmount = amounts.Gross;
        TaxRate = amounts.Rate;
        PricesIncludeTax = amounts.PricesIncludeTax;
        Currency = currency;
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;
}
