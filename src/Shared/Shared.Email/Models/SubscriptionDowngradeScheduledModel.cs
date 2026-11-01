namespace Shared.Email.Models;

public sealed class SubscriptionDowngradeScheduledModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string CurrentPlanName { get; set; } = string.Empty;
    public string NewPlanName { get; set; } = string.Empty;
    public string EffectiveAt { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["CurrentPlanName"] = CurrentPlanName,
        ["NewPlanName"] = NewPlanName,
        ["EffectiveAt"] = EffectiveAt,
        ["PortalUrl"] = PortalUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionDowngradeScheduled;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم جدولة تخفيض اشتراكك"
            : "Downgrade Scheduled for Your Subscription";
    }
}
