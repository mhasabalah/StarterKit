using System.ComponentModel.DataAnnotations.Schema;

namespace Subscriptions.Domains.Subscriptions.Models;

public class SubscriptionInvoice : Aggregate<Guid>
{
    public Guid SubscriptionId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? OriginalPlanId { get; private set; }
    public string InvoiceNumber { get; private set; } = default!;
    public InvoiceType Type { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public decimal TaxRate { get; private set; }
    public string Currency { get; private set; } = default!;
    public bool IsDeferred { get; private set; }
    public bool IsVoided { get; private set; }
    public string? PdfStorageKey { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset IssuedAt { get; private set; }
    public new DateTimeOffset CreatedAt { get; private set; }

    [NotMapped] public decimal NetAmount => Math.Round(Amount * (1 - TaxRate), 2);
    [NotMapped] public decimal TaxAmount => Math.Round(Amount * TaxRate, 2);
    [NotMapped] public decimal TotalAmount => Amount;

    public Subscription Subscription { get; private set; } = null!;

    private SubscriptionInvoice()
    {
    }

    public static SubscriptionInvoice Create(
        Guid subscriptionId,
        Guid? paymentId,
        string invoiceNumber,
        InvoiceType type,
        decimal amount,
        string currency,
        bool isDeferred,
        Guid? originalPlanId = null,
        decimal taxRate = 0.15m)
    {
        return new SubscriptionInvoice
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            PaymentId = paymentId,
            OriginalPlanId = originalPlanId,
            InvoiceNumber = invoiceNumber,
            Type = type,
            Status = InvoiceStatus.Pending,
            Amount = amount,
            TaxRate = taxRate,
            Currency = currency,
            IsDeferred = isDeferred,
            IsVoided = false,
            RetryCount = 0,
            IssuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkGenerated(string pdfStorageKey)
    {
        Status = InvoiceStatus.Generated;
        PdfStorageKey = pdfStorageKey;
        ErrorMessage = null;
    }

    public void MarkSent()
    {
        Status = InvoiceStatus.Sent;
        SentAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string errorMessage)
    {
        Status = InvoiceStatus.Failed;
        RetryCount++;
        ErrorMessage = errorMessage.Length > 500 ? errorMessage[..500] : errorMessage;
    }

    public void Retry()
    {
        Status = InvoiceStatus.Pending;
        ErrorMessage = null;
    }

    public void Void()
    {
        IsVoided = true;
    }
}
