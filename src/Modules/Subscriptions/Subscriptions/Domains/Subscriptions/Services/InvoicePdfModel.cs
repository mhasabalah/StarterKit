using Subscriptions.Domains.Subscriptions.Models;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Flat, presentation-ready snapshot of an invoice used to render the PDF.
/// Deliberately free of EF entities so the layout can be unit-tested in isolation.
/// </summary>
public sealed record InvoicePdfModel
{
    public required string InvoiceNumber { get; init; }
    public required string TypeLabel { get; init; }
    public required string Description { get; init; }
    public required string SubscriberName { get; init; }
    public required string Email { get; init; }
    public string? BillingAddress { get; init; }
    public string? TaxId { get; init; }
    public required string Status { get; init; }
    public required string Currency { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required decimal NetAmount { get; init; }
    public required decimal TaxAmount { get; init; }
    public required decimal TotalAmount { get; init; }
    public required int VatPercent { get; init; }
    public required bool IsDeferred { get; init; }

    /// <summary>Seller branding; defaults to the generic StarterKit values.</summary>
    public InvoiceBrandingOptions Seller { get; init; } = new();

    public static InvoicePdfModel From(
        SubscriptionInvoice invoice,
        Subscription subscription,
        SubscriberInfo? subscriber,
        InvoiceBrandingOptions seller)
    {
        var description = invoice.Type switch
        {
            InvoiceType.Activation => $"Subscription Activation — {subscription.PlanName}",
            InvoiceType.Renewal => $"Subscription Renewal — {subscription.PlanName}",
            InvoiceType.Proration => $"Proration Adjustment — {subscription.PlanName}",
            _ => subscription.PlanName
        };

        return new InvoicePdfModel
        {
            InvoiceNumber = invoice.InvoiceNumber,
            TypeLabel = invoice.Type.ToString(),
            Description = description,
            SubscriberName = subscriber?.BillingName ?? subscriber?.DisplayName ?? "N/A",
            Email = subscriber?.Email ?? "—",
            BillingAddress = subscriber?.BillingAddress,
            TaxId = subscriber?.TaxId,
            Status = invoice.Status.ToString(),
            Currency = invoice.Currency,
            IssuedAt = invoice.IssuedAt,
            NetAmount = invoice.NetAmount,
            TaxAmount = invoice.TaxAmount,
            TotalAmount = invoice.TotalAmount,
            VatPercent = (int)(invoice.TaxRate * 100),
            IsDeferred = invoice.IsDeferred,
            Seller = seller
        };
    }
}
