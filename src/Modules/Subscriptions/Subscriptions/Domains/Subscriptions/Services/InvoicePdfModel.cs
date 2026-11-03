using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Flat, presentation-ready snapshot of an invoice used to render the PDF.
/// Deliberately free of EF entities so the layout can be unit-tested in isolation.
/// Built only from the invoice's issue-time snapshot (<see cref="FromSnapshot"/>), never from live plan/subscriber data.
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

    /// <summary>Decimal fraction snapshotted at issue time (0.075 = 7.5%).</summary>
    public required decimal TaxRate { get; init; }

    public bool PricesIncludeTax { get; init; } = true;
    public bool IsCreditNote { get; init; }

    /// <summary>Rate as printed, without truncation ("7.5%").</summary>
    public string TaxRateLabel => TaxCalculator.FormatRate(TaxRate);

    /// <summary>Seller details; name/address/tax id come from the invoice snapshot.</summary>
    public InvoiceBrandingOptions Seller { get; init; } = new();

    public static InvoicePdfModel FromSnapshot(SubscriptionInvoice invoice, InvoiceBrandingOptions branding) => new()
    {
        InvoiceNumber = invoice.InvoiceNumber,
        TypeLabel = invoice.IsCreditNote ? "Credit Note" : invoice.Type.ToString(),
        Description = invoice.Description,
        SubscriberName = invoice.BuyerName,
        Email = invoice.BuyerEmail ?? "-",
        BillingAddress = invoice.BuyerAddress,
        TaxId = invoice.BuyerTaxId,
        Status = invoice.IsCreditNote ? "Credited" : "Issued",
        Currency = invoice.Currency,
        IssuedAt = invoice.IssuedAt,
        NetAmount = invoice.NetAmount,
        TaxAmount = invoice.TaxAmount,
        TotalAmount = invoice.TotalAmount,
        TaxRate = invoice.TaxRate,
        PricesIncludeTax = invoice.PricesIncludeTax,
        IsCreditNote = invoice.IsCreditNote,
        Seller = new InvoiceBrandingOptions
        {
            // Legal identity from the snapshot; contact/marketing lines from current branding.
            CompanyName = invoice.SellerName,
            Address = invoice.SellerAddress,
            TaxId = invoice.SellerTaxId,
            Tagline = branding.Tagline,
            Website = branding.Website,
            SupportEmail = branding.SupportEmail,
            NumberPrefix = branding.NumberPrefix
        }
    };
}
