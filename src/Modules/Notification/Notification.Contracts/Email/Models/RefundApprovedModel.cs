namespace Notification.Contracts.Email.Models;

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
}
