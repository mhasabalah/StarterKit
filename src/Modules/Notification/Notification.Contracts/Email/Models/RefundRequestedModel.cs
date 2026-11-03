namespace Notification.Contracts.Email.Models;

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
}
