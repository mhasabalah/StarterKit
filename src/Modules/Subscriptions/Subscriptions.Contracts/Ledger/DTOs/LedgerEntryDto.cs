namespace Subscriptions.Contracts.Ledger.DTOs;

/// <summary>One immutable ledger row. Amount is signed (Payment &gt; 0, Refund/CreditNote &lt; 0).</summary>
public record LedgerEntryDto(
    Guid EntryId,
    DateTimeOffset OccurredAt,
    string Type,
    decimal Amount,
    string Currency,
    Guid SubscriptionId,
    Guid SubscriberId,
    Guid? PaymentId,
    Guid? InvoiceId,
    Guid? RefundId,
    Guid ActorId,
    string Description,
    Guid? PlanId,
    string? PlanName,
    string? PaymentMethod,
    string? Reference);
