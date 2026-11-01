namespace Shared.Email.Models;

public sealed class PriceChangeNotificationModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string OldPrice { get; set; } = string.Empty;
    public string NewPrice { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string EffectiveAt { get; set; } = string.Empty;
    public string DaysUntilEffective { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["OldPrice"] = OldPrice,
        ["NewPrice"] = NewPrice,
        ["Currency"] = Currency,
        ["EffectiveAt"] = EffectiveAt,
        ["DaysUntilEffective"] = DaysUntilEffective,
        ["PortalUrl"] = PortalUrl
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.PriceChangeNotification;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "إشعار بتغيير سعر اشتراكك"
            : "Upcoming Price Change for Your Subscription";
    }
}
