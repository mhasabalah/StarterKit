namespace Shared.Email.Models;

public sealed class RefundApprovedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string SupportWhatsApp { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["Amount"] = Amount,
        ["Currency"] = Currency,
        ["SupportWhatsApp"] = SupportWhatsApp,
        ["SupportEmail"] = SupportEmail
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.RefundApproved;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "تمت الموافقة على طلب الاسترداد"
            : "Your Refund Has Been Approved";
    }
}
