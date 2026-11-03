namespace Notification.Contracts.Email.Models;

public sealed class SubscriptionUpgradedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string OldPlanName { get; set; } = string.Empty;
    public string NewPlanName { get; set; } = string.Empty;
    public string ProratedAmount { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string IsDeferred { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["OldPlanName"] = OldPlanName,
        ["NewPlanName"] = NewPlanName,
        ["ProratedAmount"] = ProratedAmount,
        ["Currency"] = Currency,
        ["IsDeferred"] = IsDeferred,
        ["PortalUrl"] = PortalUrl
    };
}
