namespace Subscriptions.Contracts.Subscriptions.DTOs;

public record AdminInvoiceDto(
    Guid InvoiceId,
    Guid SubscriptionId,
    Guid SubscriberId,
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
    int RetryCount,
    string? ErrorMessage,
    DateTimeOffset? SentAt,
    DateTimeOffset IssuedAt,
    DateTimeOffset CreatedAt);
