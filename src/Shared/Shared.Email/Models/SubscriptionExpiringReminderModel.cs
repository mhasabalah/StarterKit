namespace Shared.Email.Models;

/// <summary>Pure data bag. Template identity and subjects live in nested <see cref="Metadata"/>.</summary>
public sealed class SubscriptionExpiringReminderModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public string RenewalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["ExpiryDate"] = ExpiryDate,
        ["RenewalUrl"] = RenewalUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionExpiringReminder;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "اشتراكك على وشك الانتهاء"
            : "Your Subscription Is Expiring Soon";
    }
}
