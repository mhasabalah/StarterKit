namespace Shared.Email.Models;

public sealed class SubscriptionDowngradeCancelledModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["PortalUrl"] = PortalUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionDowngradeCancelled;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم إلغاء جدولة التخفيض"
            : "Scheduled Downgrade Cancelled";
    }
}
