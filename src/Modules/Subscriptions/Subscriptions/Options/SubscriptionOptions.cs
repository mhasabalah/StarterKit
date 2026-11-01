namespace Subscriptions.Options;

public class SubscriptionOptions
{
    public int GracePeriodDays { get; set; } = 7;
    public int CacheMinutes { get; set; } = 5;
    public int NoSubscriptionCacheMinutes { get; set; } = 1;
    public int UpgradeSlaHours { get; set; } = 72;
    public int ProratedDeferralThresholdPercent { get; set; } = 15;
    public int TrustMinCycles { get; set; } = 2;
    public int PriceChangeNoticeDays { get; set; } = 53;
    public Guid SystemActorId { get; set; } =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    public int RefundFullWindowDays { get; set; } = 14;
    public int RefundProratedWindowDays { get; set; } = 45;
    public decimal TaxRate { get; set; } = 0.15m;

    /// <summary>Seller branding printed on invoices (config section <c>Subscriptions:Invoice</c>).</summary>
    public InvoiceBrandingOptions Invoice { get; set; } = new();
}

/// <summary>
/// Seller/company details printed on generated invoice PDFs.
/// Override in configuration, e.g. <c>"Subscriptions": { "Invoice": { "CompanyName": "Acme" } }</c>.
/// </summary>
public class InvoiceBrandingOptions
{
    public string CompanyName { get; set; } = "StarterKit";
    public string? Tagline { get; set; }
    public string? Website { get; set; } = "example.com";
    public string? SupportEmail { get; set; } = "support@example.com";
    public string? Address { get; set; }
    public string? TaxId { get; set; }

    /// <summary>Prefix for generated invoice numbers, e.g. <c>INV</c> gives <c>INV-202601-00001</c>.</summary>
    public string NumberPrefix { get; set; } = "INV";
}
