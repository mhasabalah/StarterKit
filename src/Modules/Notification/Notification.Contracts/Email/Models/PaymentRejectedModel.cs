namespace Notification.Contracts.Email.Models;

/// <summary>Sent to the subscriber when their payment proof is rejected by admin.</summary>
public sealed class PaymentRejectedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string RejectionReason { get; set; } = string.Empty;
    public string ResubmitUrl { get; set; } = string.Empty;
    public string SupportWhatsApp { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["RejectionReason"] = RejectionReason,
        ["ResubmitUrl"] = ResubmitUrl,
        ["SupportWhatsApp"] = SupportWhatsApp,
        ["SupportEmail"] = SupportEmail
    };
}
