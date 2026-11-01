namespace Shared.Email.Models;

/// <summary>Sent to the subscriber when their subscription is cancelled.</summary>
public sealed class SubscriptionCancelledModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string CancelledDate { get; set; } = string.Empty;
    public string SupportWhatsApp { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["CancelledDate"] = CancelledDate,
        ["SupportWhatsApp"] = SupportWhatsApp,
        ["SupportEmail"] = SupportEmail
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionCancelled;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم إلغاء اشتراكك"
            : "Your Subscription Has Been Cancelled";
    }
}
