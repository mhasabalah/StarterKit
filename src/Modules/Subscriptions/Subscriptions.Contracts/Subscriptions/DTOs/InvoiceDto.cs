namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <summary>Subscriber view of an issued invoice or credit note (issue-time snapshot).</summary>
/// <param name="Amount">Gross total (same as <see cref="TotalAmount"/>; kept for older clients).</param>
/// <param name="IsVoided">True when the invoice has been fully credited by credit notes.</param>
public record InvoiceDto(
    Guid InvoiceId,
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
    Guid? OriginalInvoiceId,
    DateTimeOffset IssuedAt);
