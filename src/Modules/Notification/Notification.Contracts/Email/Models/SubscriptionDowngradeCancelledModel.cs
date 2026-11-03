namespace Notification.Contracts.Email.Models;

public sealed class SubscriptionDowngradeCancelledModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["PortalUrl"] = PortalUrl
    };
}
