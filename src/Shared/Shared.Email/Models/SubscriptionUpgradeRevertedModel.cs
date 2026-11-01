namespace Shared.Email.Models;

public sealed class SubscriptionUpgradeRevertedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["Reason"] = Reason,
        ["PortalUrl"] = PortalUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionUpgradeReverted;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم إلغاء ترقية اشتراكك"
            : "Subscription Upgrade Reverted";
    }
}
