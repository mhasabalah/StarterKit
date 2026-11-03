namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <summary>
/// Admin view of an issued invoice or credit note. All money values are the snapshot taken at issue time
/// (never recomputed from live configuration). Credit notes carry negative amounts and <see cref="OriginalInvoiceId"/>.
/// </summary>
/// <param name="Amount">Gross total (same as <see cref="TotalAmount"/>; kept for older clients).</param>
/// <param name="IsVoided">True when the invoice has been fully credited by credit notes.</param>
/// <param name="Version">Round-trip as <c>IssueCreditNoteCommand.Version</c>.</param>
public record AdminInvoiceDto(
    Guid InvoiceId,
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid? PaymentId,
    Guid? OriginalInvoiceId,
    string InvoiceNumber,
    string Type,
    string Status,
    string Description,
    decimal Amount,
    decimal TaxRate,
    bool PricesIncludeTax,
    decimal NetAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal CreditedAmount,
    string Currency,
    bool IsVoided,
    int RetryCount,
    string? ErrorMessage,
    DateTimeOffset? SentAt,
    DateTimeOffset IssuedAt,
    DateTimeOffset CreatedAt,
    long Version);
