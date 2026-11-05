using System.Text.Json.Serialization;

namespace Notification.Contracts.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmailTemplate
{
    [TemplateFile("PasswordResetRequired")]
    PasswordResetRequired,

    [TemplateFile("SubscriptionExpired")]
    SubscriptionExpired,

    [TemplateFile("SubscriptionExpiringReminder")]
    SubscriptionExpiringReminder,

    [TemplateFile("SubscriptionHardLocked")]
    SubscriptionHardLocked,

    [TemplateFile("WelcomeUser")]
    WelcomeUser,

    [TemplateFile("SubscriptionActivated")]
    SubscriptionActivated,

    [TemplateFile("SubscriptionPastDue")]
    SubscriptionPastDue,

    [TemplateFile("SubscriptionRenewed")]
    SubscriptionRenewed,

    [TemplateFile("SubscriptionCancelled")]
    SubscriptionCancelled,

    [TemplateFile("SubscriptionRejected")]
    SubscriptionRejected,

    [TemplateFile("PaymentRejected")]
    PaymentRejected,

    [TemplateFile("AdminPaymentProofReceived")]
    AdminPaymentProofReceived,

    [TemplateFile("InvoiceIssued")]
    InvoiceIssued,

    [TemplateFile("SubscriptionUpgraded")]
    SubscriptionUpgraded,

    [TemplateFile("SubscriptionUpgradeReverted")]
    SubscriptionUpgradeReverted,

    [TemplateFile("SubscriptionDowngradeScheduled")]
    SubscriptionDowngradeScheduled,

    [TemplateFile("SubscriptionDowngradeCancelled")]
    SubscriptionDowngradeCancelled,

    [TemplateFile("SubscriptionDowngradeApplied")]
    SubscriptionDowngradeApplied,

    [TemplateFile("PriceChangeNotification")]
    PriceChangeNotification,

    [TemplateFile("PriceChangeCancelled")]
    PriceChangeCancelled,

    [TemplateFile("SubscriptionCancellationScheduled")]
    SubscriptionCancellationScheduled,

    [TemplateFile("RefundRequested")]
    RefundRequested,

    [TemplateFile("RefundApproved")]
    RefundApproved,

    [TemplateFile("RefundRejected")]
    RefundRejected,

    [TemplateFile("RefundProcessed")]
    RefundProcessed,

    [TemplateFile("AdminBillingDigest")]
    AdminBillingDigest,

    [TemplateFile("TenantInvitation")]
    TenantInvitation,

    // Appended (never inserted) so stored numeric values of existing templates stay stable.
    [TemplateFile("EmailConfirmation")]
    EmailConfirmation,

    [TemplateFile("WebhookEndpointDisabled")]
    WebhookEndpointDisabled,

    [TemplateFile("TrialEnding")]
    TrialEnding,

    [TemplateFile("SubscriptionDunning")]
    SubscriptionDunning
}
