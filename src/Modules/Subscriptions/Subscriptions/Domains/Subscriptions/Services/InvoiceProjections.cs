using System.Linq.Expressions;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>EF-translatable projections of the issue-time invoice snapshot (never recomputed from live config).</summary>
public static class InvoiceProjections
{
    public static readonly Expression<Func<SubscriptionInvoice, InvoiceDto>> ToInvoiceDto = x => new InvoiceDto(
        x.Id,
        x.InvoiceNumber,
        x.Type.ToString(),
        x.Status.ToString(),
        x.Description,
        x.TotalAmount,
        x.TaxRate,
        x.PricesIncludeTax,
        x.NetAmount,
        x.TaxAmount,
        x.TotalAmount,
        x.CreditedAmount,
        x.Currency,
        x.Type != InvoiceType.CreditNote && x.CreditedAmount >= x.TotalAmount,
        x.OriginalInvoiceId,
        x.IssuedAt);
}
