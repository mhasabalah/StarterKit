namespace Subscriptions.Contracts.Subscriptions.Commands;

public record ApproveSubscriptionResult(
    Guid SubscriptionId,
    string Status,
    DateTimeOffset StartDate,
    DateTimeOffset RenewalDate,
    Guid PaymentId,
    Guid InvoiceId,
    string InvoiceNumber);
