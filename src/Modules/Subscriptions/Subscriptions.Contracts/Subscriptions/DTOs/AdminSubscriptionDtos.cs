using Subscriptions.Contracts.Ledger.DTOs;

namespace Subscriptions.Contracts.Subscriptions.DTOs;

/// <summary>One row of the admin subscription search (<c>GET admin/subscriptions</c>).</summary>
/// <param name="SubscriberName">Denormalized snapshot (refreshed on create/approval); display/search only.</param>
/// <param name="Price">Period price snapshot (tax basis per <c>Subscriptions:Tax:PricesIncludeTax</c>).</param>
/// <param name="ComplimentaryUntil">End of an admin-granted complimentary period, if any.</param>
/// <param name="Version">Round-trip as the Version of admin subscription commands.</param>
public record AdminSubscriptionListItemDto(
    Guid SubscriptionId,
    Guid SubscriberId,
    string? SubscriberName,
    string? SubscriberEmail,
    Guid PlanId,
    string PlanName,
    string Status,
    decimal Price,
    string Currency,
    int BillingCycleInMonths,
    DateTimeOffset? StartDate,
    DateTimeOffset? RenewalDate,
    DateTimeOffset? GracePeriodEndsAt,
    bool CancelledAtPeriodEnd,
    DateTimeOffset? ComplimentaryUntil,
    DateTimeOffset UpdatedAt,
    long Version);

/// <summary>Subscriber-360: everything billing knows about one subscription (<c>GET admin/subscriptions/{id}</c>).</summary>
/// <param name="Invoices">Issued invoices (not credit notes), newest first.</param>
/// <param name="CreditNotes">Credit notes, newest first.</param>
/// <param name="LedgerTotals">Money totals per currency over the whole ledger of this subscription.</param>
/// <param name="Limits">The subscriber's active plan limits (<c>GetActivePlanLimitsQuery</c>).</param>
public record AdminSubscriptionDetailsDto(
    AdminSubscriptionDetailDto Subscription,
    IReadOnlyList<AdminSubscriptionPaymentDto> Payments,
    IReadOnlyList<AdminInvoiceDto> Invoices,
    IReadOnlyList<AdminInvoiceDto> CreditNotes,
    IReadOnlyList<AdminRefundRequestDto> Refunds,
    IReadOnlyList<SubscriptionTransitionDto> Transitions,
    IReadOnlyList<AdminScheduledPlanChangeDto> ScheduledChanges,
    IReadOnlyList<LedgerEntryDto> Ledger,
    IReadOnlyList<RevenueSummaryDto> LedgerTotals,
    ActivePlanLimitsDto Limits);

/// <param name="DeferredProrationAmount">Upgrade proration deferred to the next renewal (tax basis).</param>
public record AdminSubscriptionDetailDto(
    Guid SubscriptionId,
    Guid SubscriberId,
    string? SubscriberName,
    string? SubscriberEmail,
    Guid PlanId,
    string PlanName,
    string Status,
    decimal Price,
    string Currency,
    int BillingCycleInMonths,
    int? MaxItems,
    int? MaxStorageGb,
    int? MaxUsers,
    DateTimeOffset? StartDate,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? RenewalDate,
    DateTimeOffset? GracePeriodEndsAt,
    DateTimeOffset? EndDate,
    DateTimeOffset? CancelledAt,
    bool CancelledAtPeriodEnd,
    DateTimeOffset? ScheduledCancellationDate,
    string? CancellationReason,
    string? CancellationNote,
    decimal DeferredProrationAmount,
    DateTimeOffset? ComplimentaryUntil,
    DateTimeOffset UpdatedAt,
    long Version);

/// <param name="ProofContentHash">SHA-256 of the latest proof file.</param>
/// <param name="DuplicateProof">The proof has the same content as another payment's proof (warning only).</param>
/// <param name="Version">The payment version (proration approve/reject Version).</param>
public record AdminSubscriptionPaymentDto(
    Guid PaymentId,
    string Purpose,
    string Status,
    decimal Amount,
    decimal AmountReceived,
    decimal OutstandingAmount,
    string Currency,
    PaymentMethodSnapshotDto? PaymentMethod,
    Guid? ProofAttachmentId,
    string? ProofContentHash,
    bool DuplicateProof,
    Guid? DuplicateOfPaymentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? PaidAt,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? Notes,
    long Version,
    IReadOnlyList<PaymentReceiptDto> Receipts);

/// <summary>A manual (offline) receipt recorded against a payment.</summary>
public record PaymentReceiptDto(
    Guid ReceiptId,
    decimal Amount,
    string Currency,
    string Method,
    string? Reference,
    DateTimeOffset ReceivedAt,
    string? Note,
    Guid RecordedBy,
    DateTimeOffset RecordedAt);

public record SubscriptionTransitionDto(
    string? FromStatus,
    string ToStatus,
    Guid ActorId,
    string ActorType,
    string? Reason,
    DateTimeOffset OccurredAt);

public record AdminScheduledPlanChangeDto(
    Guid ChangeId,
    string ChangeType,
    string Status,
    Guid NewPlanId,
    string NewPlanName,
    decimal NewPrice,
    DateTimeOffset ScheduledAt,
    DateTimeOffset EffectiveAt,
    DateTimeOffset? AppliedAt);
