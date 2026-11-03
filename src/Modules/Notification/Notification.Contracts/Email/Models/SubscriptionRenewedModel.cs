namespace Notification.Contracts.Email.Models;

/// <summary>Sent to the subscriber when a renewal payment is approved.</summary>
public sealed class SubscriptionRenewedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string NewRenewalDate { get; set; } = string.Empty;
    public string DashboardUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["NewRenewalDate"] = NewRenewalDate,
        ["DashboardUrl"] = DashboardUrl
    };
}
