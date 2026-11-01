namespace Shared.Email.Models;

/// <summary>Pure data bag. Template identity and subjects live in nested <see cref="Metadata"/>.</summary>
public sealed class SubscriptionExpiredModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string GracePeriodEndsAt { get; set; } = string.Empty;
    public string RenewalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["GracePeriodEndsAt"] = GracePeriodEndsAt,
        ["RenewalUrl"] = RenewalUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionExpired;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "انتهت صلاحية اشتراكك"
            : "Your Subscription Has Expired";
    }
}
