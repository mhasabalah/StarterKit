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

    /// <summary>Plan entitlement enforcement (section <c>Subscriptions:Entitlements</c>).</summary>
    public EntitlementOptions Entitlements { get; set; } = new();

    /// <summary>Manual / partial payments recorded by admins (section <c>Subscriptions:ManualPayments</c>).</summary>
    public ManualPaymentOptions ManualPayments { get; set; } = new();

    /// <summary>Who receives admin billing emails (section <c>Subscriptions:AdminNotifications</c>).</summary>
    public AdminNotificationOptions AdminNotifications { get; set; } = new();

    /// <summary>
    /// Online payment providers by name (section <c>Subscriptions:PaymentGateways:&lt;Name&gt;</c>). A gateway is usable
    /// only when its adapter is registered AND <c>Enabled</c> is true here.
    /// </summary>
    public Dictionary<string, PaymentGatewayOptions> PaymentGateways { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Origins (scheme://host[:port]) a checkout's success/cancel URL may point to, so the hosted checkout can't be used
    /// as an open redirect. Empty → <c>Cors:AllowedOrigins</c> (the front ends). Both empty (dev only) → any http(s) URL.
    /// </summary>
    public string[] OnlinePaymentReturnOrigins { get; set; } = [];

    /// <summary>Inbound payment webhook inbox (section <c>Subscriptions:PaymentWebhooks</c>).</summary>
    public PaymentWebhookOptions PaymentWebhooks { get; set; } = new();

    /// <summary>Free trials (section <c>Subscriptions:Trials</c>).</summary>
    public TrialOptions Trials { get; set; } = new();

    /// <summary>Overdue-renewal reminders (section <c>Subscriptions:Dunning</c>).</summary>
    public DunningOptions Dunning { get; set; } = new();

    /// <summary>Usage metering and quotas (section <c>Subscriptions:Metering</c>).</summary>
    public MeteringOptions Metering { get; set; } = new();

    /// <summary>Tax configuration (section <c>Subscriptions:Tax</c>). The ONLY source of the tax rate.</summary>
    public TaxOptions Tax { get; set; } = new();

    /// <summary>Seller branding printed on invoices (config section <c>Subscriptions:Invoice</c>).</summary>
    public InvoiceBrandingOptions Invoice { get; set; } = new();
}

/// <summary>Section <c>Subscriptions:Trials</c>.</summary>
public class TrialOptions
{
    /// <summary>The "trial ending" email goes out this many days before the trial ends.</summary>
    public int ReminderDaysBeforeEnd { get; set; } = 3;

    /// <summary>
    /// When a trial ends, the first paid period is billed and the subscriber has this many days to pay (PastDue) before
    /// the subscription expires. Shorter than the regular grace period: nothing was ever paid.
    /// </summary>
    public int PostTrialGraceDays { get; set; } = 3;

    /// <summary>Cron (UTC) of the <c>process-trial-reminders</c> job. Default daily 09:30.</summary>
    public string ReminderCron { get; set; } = "30 9 * * *";
}

/// <summary>
/// Section <c>Subscriptions:Dunning</c>: while a renewal is overdue (PastDue) the subscriber is reminded on
/// <see cref="ScheduleDays"/> (days after it went past due), each time with a fresh checkout link, and once more
/// <see cref="FinalNoticeDaysBeforeGraceEnd"/> days before the grace period ends. Each step is recorded and never re-sent.
/// Automatic card retries are NOT part of this (they need stored payment methods).
/// </summary>
public class DunningOptions
{
    public static readonly int[] DefaultScheduleDays = [1, 3, 5];

    public bool Enabled { get; set; } = true;

    /// <summary>Days after past due to remind on. Null/empty = <see cref="DefaultScheduleDays"/> (1, 3, 5).</summary>
    public int[]? ScheduleDays { get; set; }

    /// <summary>The last reminder goes out this many days before the grace period ends.</summary>
    public int FinalNoticeDaysBeforeGraceEnd { get; set; } = 1;

    /// <summary>Cron (UTC) of the <c>process-dunning</c> job. Default hourly at :20.</summary>
    public string Cron { get; set; } = "20 * * * *";

    public IReadOnlyList<int> EffectiveScheduleDays =>
        ScheduleDays is { Length: > 0 } days ? days : DefaultScheduleDays;
}

/// <summary>Section <c>Subscriptions:Metering</c>.</summary>
public class MeteringOptions
{
    /// <summary>Cron (UTC) of the <c>flush-usage-counters</c> job. Default hourly at :05.</summary>
    public string FlushCron { get; set; } = "5 * * * *";

    /// <summary>Days a monthly Redis counter lives after its last increment (the database keeps the record).</summary>
    public int MonthlyCounterTtlDays { get; set; } = 62;
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

/// <summary>Configuration section <c>Subscriptions:Entitlements</c>.</summary>
public class EntitlementOptions
{
    /// <summary>
    /// When false (default) <c>.RequireFeature(...)</c> allows every request and plan limits read as unlimited; the
    /// would-be denial is logged at debug level. Turn on once plans carry the right features.
    /// </summary>
    public bool Enforce { get; set; }

    /// <summary>Seconds a subscriber's entitlements stay cached (events invalidate sooner).</summary>
    public int CacheSeconds { get; set; } = 60;
}

/// <summary>One entry of <c>Subscriptions:PaymentGateways</c>. Secrets come from user-secrets / env / Docker secrets.</summary>
public class PaymentGatewayOptions
{
    public bool Enabled { get; set; }

    /// <summary>Webhook signing secret (and/or API key, per adapter).</summary>
    public string? Secret { get; set; }

    /// <summary>Provider base URL; the Fake adapter uses it for the hosted checkout page, Paymob for its API host.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Secret API key used for outbound calls (checkout, refund): Stripe <c>sk_...</c>, Paymob secret key. Distinct from
    /// <see cref="Secret"/>, which only verifies inbound webhooks (Stripe <c>whsec_...</c>, Paymob HMAC secret).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Paymob public key (<c>..._pk_...</c>) for the unified checkout URL.</summary>
    public string? PublicKey { get; set; }

    /// <summary>Paymob payment integration ids (card, wallet, ...) offered at checkout.</summary>
    public long[] IntegrationIds { get; set; } = [];

    /// <summary>Optional server-to-server callback URL sent with each Paymob intention (else the dashboard setting is used).</summary>
    public string? NotificationUrl { get; set; }

    /// <summary>Stripe: how old a webhook's signed timestamp may be before it is refused as a replay.</summary>
    public int WebhookToleranceSeconds { get; set; } = 300;
}

/// <summary>Section <c>Subscriptions:PaymentWebhooks</c>.</summary>
public class PaymentWebhookOptions
{
    /// <summary>Processing attempts (inline + retries) before an event stays failed for an admin.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>A failed event is retried by the job once its last attempt is older than attempts x this many minutes.</summary>
    public int RetryBackoffMinutes { get; set; } = 2;

    /// <summary>Cron (UTC) of the <c>retry-failed-payment-webhooks</c> job. Default every 5 minutes.</summary>
    public string RetryCron { get; set; } = "*/5 * * * *";
}
