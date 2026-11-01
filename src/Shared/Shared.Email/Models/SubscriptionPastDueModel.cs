namespace Shared.Email.Models;

/// <summary>Sent to the subscriber when their subscription enters the grace period (past due).</summary>
public sealed class SubscriptionPastDueModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string GraceDeadline { get; set; } = string.Empty;
    public string RenewalUrl { get; set; } = string.Empty;
    public string SupportWhatsApp { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["GraceDeadline"] = GraceDeadline,
        ["RenewalUrl"] = RenewalUrl,
        ["SupportWhatsApp"] = SupportWhatsApp,
        ["SupportEmail"] = SupportEmail
    };

    public sealed class Metadata : IEmailTemplate
    {
        public EmailTemplate Template => EmailTemplate.SubscriptionPastDue;

        public string GetSubject(EmailCulture culture) => culture == EmailCulture.Ar
            ? "اشتراكك متأخر — يُرجى التجديد خلال فترة السماح"
            : "Subscription Past Due — Renew Within Grace Period";
    }
}
