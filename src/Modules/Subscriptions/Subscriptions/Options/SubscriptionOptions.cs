using Subscriptions.Domains.Subscriptions.Billing;

namespace Subscriptions.Options;

/// <summary>Configuration section <c>Subscriptions</c>.</summary>
public class SubscriptionOptions
{
    public int GracePeriodDays { get; set; } = 7;
    public int CacheMinutes { get; set; } = 5;
    public int NoSubscriptionCacheMinutes { get; set; } = 1;

    /// <summary>Hours a subscriber has to get an upgrade proration paid (proof approved) before the upgrade is reverted.</summary>
    public int UpgradeSlaHours { get; set; } = 72;

    public int ProratedDeferralThresholdPercent { get; set; } = 15;
    public int TrustMinCycles { get; set; } = 2;
    public int PriceChangeNoticeDays { get; set; } = 53;
    public Guid SystemActorId { get; set; } =
        Guid.Parse("00000000-0000-0000-0000-000000000001");
    public int RefundFullWindowDays { get; set; } = 14;
    public int RefundProratedWindowDays { get; set; } = 45;

    /// <summary>An admin may reactivate a Cancelled/Expired subscription that ended at most this many days ago.</summary>
    public int ReactivationWindowDays { get; set; } = 30;

    /// <summary>Minutes the admin billing dashboard is cached per date range (no invalidation; KPIs may lag).</summary>
    public int BillingDashboardCacheMinutes { get; set; } = 5;

    /// <summary>Manual / partial payments recorded by admins (section <c>Subscriptions:ManualPayments</c>).</summary>
    public ManualPaymentOptions ManualPayments { get; set; } = new();

    /// <summary>Who receives admin billing emails (section <c>Subscriptions:AdminNotifications</c>).</summary>
    public AdminNotificationOptions AdminNotifications { get; set; } = new();

    /// <summary>Tax configuration (section <c>Subscriptions:Tax</c>). The ONLY source of the tax rate.</summary>
    public TaxOptions Tax { get; set; } = new();

    /// <summary>Seller branding printed on invoices (config section <c>Subscriptions:Invoice</c>).</summary>
    public InvoiceBrandingOptions Invoice { get; set; } = new();
}

/// <summary>Section <c>Subscriptions:ManualPayments</c>.</summary>
public class ManualPaymentOptions
{
    /// <summary>
    /// False (default): a manual receipt larger than the outstanding amount is rejected (400). True: it is accepted,
    /// the payment completes and the excess stays recorded in the ledger as received money (credit to be settled by
    /// an admin with a refund; it is not applied automatically).
    /// </summary>
    public bool AllowOverpaymentCredit { get; set; }
}

/// <summary>
/// Section <c>Subscriptions:AdminNotifications</c>. Recipients of "new payment proof", "refund requested" and the
/// daily billing digest: <see cref="Emails"/> when set; otherwise every active user in <see cref="Roles"/>
/// (resolved through the user-directory port <c>GetUsersInRoleQuery</c>).
/// </summary>
public class AdminNotificationOptions
{
    public static readonly string[] DefaultRoles = ["Admin", "Finance"];

    /// <summary>Explicit recipient list. Wins over <see cref="Roles"/> when not empty.</summary>
    public string[]? Emails { get; set; }

    /// <summary>Roles whose active users receive the emails when <see cref="Emails"/> is empty. Default Admin + Finance.</summary>
    public string[]? Roles { get; set; }

    /// <summary>The digest lists submitted proofs waiting longer than this many hours.</summary>
    public int DigestStaleProofHours { get; set; } = 24;

    /// <summary>Cron (UTC) of the <c>admin-billing-digest</c> job. Default daily 07:00.</summary>
    public string DigestCron { get; set; } = "0 7 * * *";

    public IReadOnlyList<string> EffectiveRoles =>
        Roles is { Length: > 0 } roles ? roles : DefaultRoles;
}

/// <summary>
/// Tax settings (<c>Subscriptions:Tax</c>). Invoices snapshot the values in force at issue time, so changing them
/// never alters issued invoices.
/// </summary>
public class TaxOptions
{
    /// <summary>True (default): plan prices are tax-inclusive (gross). False: prices are net and tax is added.</summary>
    public bool PricesIncludeTax { get; set; } = true;

    /// <summary>Decimal fraction, e.g. 0.15 = 15%, 0.075 = 7.5%. Must be within [0, 1].</summary>
    public decimal Rate { get; set; } = 0.15m;

    public TaxPolicy ToPolicy() => new(Rate, PricesIncludeTax);
}

/// <summary>
/// Seller/company details printed on generated invoice PDFs.
/// Override in configuration, e.g. <c>"Subscriptions": { "Invoice": { "CompanyName": "Acme" } }</c>.
/// Name, address and tax id are snapshotted onto each invoice at issue time.
/// </summary>
public class InvoiceBrandingOptions
{
    public string CompanyName { get; set; } = "StarterKit";
    public string? Tagline { get; set; }
    public string? Website { get; set; } = "example.com";
    public string? SupportEmail { get; set; } = "support@example.com";
    public string? Address { get; set; }
    public string? TaxId { get; set; }

    /// <summary>Prefix of invoice and credit note numbers (max 20 chars), e.g. <c>INV</c> gives <c>INV-2026-000001</c>.</summary>
    public string NumberPrefix { get; set; } = "INV";
}
