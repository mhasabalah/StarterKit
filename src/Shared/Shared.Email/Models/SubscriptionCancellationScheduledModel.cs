namespace Shared.Email.Models;

public sealed class SubscriptionCancellationScheduledModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string CancellationDate { get; set; } = string.Empty;
    public string SupportWhatsApp { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["CancellationDate"] = CancellationDate,
        ["SupportWhatsApp"] = SupportWhatsApp,
        ["SupportEmail"] = SupportEmail
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionCancellationScheduled;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم جدولة إلغاء اشتراكك"
            : "Your Subscription Cancellation Has Been Scheduled";
    }
}
