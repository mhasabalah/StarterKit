namespace Notification.Contracts.Email.Models;

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
}
