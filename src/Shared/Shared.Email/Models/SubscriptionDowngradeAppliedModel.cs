namespace Shared.Email.Models;

public sealed class SubscriptionDowngradeAppliedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string NewPlanName { get; set; } = string.Empty;
    public string NewRenewalDate { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["NewPlanName"] = NewPlanName,
        ["NewRenewalDate"] = NewRenewalDate,
        ["PortalUrl"] = PortalUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionDowngradeApplied;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم تطبيق خطتك الجديدة"
            : "Plan Updated — Subscription Renewed";
    }
}
