namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record InvoiceDto(
    Guid InvoiceId,
    string InvoiceNumber,
    string Type,
    string Status,
    decimal Amount,
    decimal TaxRate,
    decimal NetAmount,
    decimal TaxAmount,
    string Currency,
    bool IsDeferred,
    bool IsVoided,
    DateTimeOffset IssuedAt);
