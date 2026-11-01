namespace Shared.Email.Models;

/// <summary>Sent to the subscriber when a subscription or renewal is rejected by admin.</summary>
public sealed class SubscriptionRejectedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;
    public string SupportWhatsApp { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["RejectionReason"] = RejectionReason,
        ["SupportWhatsApp"] = SupportWhatsApp,
        ["SupportEmail"] = SupportEmail
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionRejected;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم رفض طلب الاشتراك"
            : "Subscription Request Rejected";
    }
}
