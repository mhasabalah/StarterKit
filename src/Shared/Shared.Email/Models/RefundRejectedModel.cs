namespace Shared.Email.Models;

public sealed class RefundRejectedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string SupportWhatsApp { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["Reason"] = Reason,
        ["SupportWhatsApp"] = SupportWhatsApp,
        ["SupportEmail"] = SupportEmail
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.RefundRejected;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تم رفض طلب الاسترداد"
            : "Your Refund Request Has Been Rejected";
    }
}
