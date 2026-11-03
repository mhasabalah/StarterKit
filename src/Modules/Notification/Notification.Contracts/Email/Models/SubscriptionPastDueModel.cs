namespace Notification.Contracts.Email.Models;

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
}
