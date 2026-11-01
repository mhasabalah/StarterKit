namespace Shared.Email.Models;

/// <summary>Sent to the subscriber when their subscription becomes active.</summary>
public sealed class SubscriptionActivatedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string RenewalDate { get; set; } = string.Empty;
    public string DashboardUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["RenewalDate"] = RenewalDate,
        ["DashboardUrl"] = DashboardUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionActivated;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم تفعيل اشتراكك بنجاح"
            : "Your Subscription Is Now Active";
    }
}
