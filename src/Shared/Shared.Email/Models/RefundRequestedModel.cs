namespace Shared.Email.Models;

public sealed class RefundRequestedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string RefundType { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["Amount"] = Amount,
        ["Currency"] = Currency,
        ["RefundType"] = RefundType
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.RefundRequested;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "طلب استرداد جديد يحتاج مراجعة"
            : "New Refund Request Awaiting Review";
    }
}
