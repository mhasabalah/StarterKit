using System.Linq.Expressions;
using Subscriptions.Contracts.Ledger.DTOs;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>EF-translatable admin projections shared by the list, search and Subscriber-360 queries (one shape each).</summary>
public static class AdminBillingProjections
{
    /// <summary>Issue-time snapshot only: amounts are never recomputed from the current tax configuration.</summary>
    public static readonly Expression<Func<SubscriptionInvoice, AdminInvoiceDto>> ToAdminInvoiceDto = i => new AdminInvoiceDto(
        i.Id,
        i.SubscriptionId,
        i.SubscriberId,
        i.PaymentId,
        i.OriginalInvoiceId,
        i.InvoiceNumber,
        i.Type.ToString(),
        i.Status.ToString(),
        i.Description,
        i.TotalAmount,
        i.TaxRate,
        i.PricesIncludeTax,
        i.NetAmount,
        i.TaxAmount,
        i.TotalAmount,
        i.CreditedAmount,
        i.Currency,
        i.Type != InvoiceType.CreditNote && i.CreditedAmount >= i.TotalAmount,
        i.RetryCount,
        i.ErrorMessage,
        i.SentAt,
        i.IssuedAt,
        i.CreatedAt,
        i.Version);

    public static readonly Expression<Func<SubscriptionRefundRequest, AdminRefundRequestDto>> ToAdminRefundRequestDto = x => new AdminRefundRequestDto(
        x.Id,
        x.SubscriptionId,
        x.SubscriberId,
        x.PlanName,
        x.BillingCycleInMonths,
        x.SubscriptionStartedAt,
        x.CancelledAt,
        x.RenewalDate,
        x.RequestedAmount,
        x.Currency,
        x.RefundType.ToString(),
        x.Status.ToString(),
        x.RequestedAt,
        x.ReviewedBy,
        x.ReviewedAt,
        x.ReviewNotes,
        x.ProcessedAt,
        x.ExternalTransactionRef,
        x.Version);

    public static readonly Expression<Func<LedgerEntry, LedgerEntryDto>> ToLedgerEntryDto = x => new LedgerEntryDto(
        x.Id,
        x.OccurredAt,
        x.Type.ToString(),
        x.Amount,
        x.Currency,
        x.SubscriptionId,
        x.SubscriberId,
        x.PaymentId,
        x.InvoiceId,
        x.RefundId,
        x.ActorId,
        x.Description,
        x.PlanId,
        x.PlanName,
        x.PaymentMethod,
        x.Reference);

    public static readonly Expression<Func<Subscription, AdminSubscriptionListItemDto>> ToAdminSubscriptionListItemDto = x => new AdminSubscriptionListItemDto(
        x.Id,
        x.SubscriberId,
        x.SubscriberDisplayName,
        x.SubscriberEmail,
        x.PlanId,
        x.PlanName,
        x.Status.ToString(),
        x.Price,
        x.Currency,
        x.BillingCycleInMonths,
        x.StartDate,
        x.RenewalDate,
        x.GracePeriodEndsAt,
        x.CancelledAtPeriodEnd,
        x.ComplimentaryUntil,
        x.UpdatedAt,
        x.Version);
}
