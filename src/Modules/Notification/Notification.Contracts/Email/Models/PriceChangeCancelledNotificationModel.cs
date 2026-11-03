namespace Notification.Contracts.Email.Models;

public sealed class PriceChangeCancelledNotificationModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string CurrentPrice { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["CurrentPrice"] = CurrentPrice,
        ["Currency"] = Currency,
        ["PortalUrl"] = PortalUrl
    };
}
