using System.Text.Json.Serialization;

namespace Notification.Contracts.Enums;

/// <summary>
/// Typed identifiers for every WhatsApp Cloud API template used by the system.
/// Each member must carry a <see cref="WhatsAppKeyAttribute"/> whose value matches
/// a key under <c>WhatsApp:Templates</c> in configuration.
/// <c>WhatsAppTemplateRegistry</c> (Notification module) validates
/// full coverage at startup.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WhatsAppTemplate
{
    [WhatsAppKey("phone_verification_otp")] PhoneVerificationOtp,
    [WhatsAppKey("two_factor_otp")] TwoFactorOtp,
    [WhatsAppKey("subscription_activated")] SubscriptionActivated,
    [WhatsAppKey("subscription_rejected")] SubscriptionRejected,
    [WhatsAppKey("subscription_past_due")] SubscriptionPastDue,
    [WhatsAppKey("subscription_renewed")] SubscriptionRenewed,
    [WhatsAppKey("subscription_expired")] SubscriptionExpired,
    [WhatsAppKey("payment_rejected")] PaymentRejected,
}
