namespace Notification.Contracts.Email.Models;

/// <summary>Pure data bag; subject and template file live in the Notification module.</summary>
public sealed class SubscriptionExpiredModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string GracePeriodEndsAt { get; set; } = string.Empty;
    public string RenewalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["GracePeriodEndsAt"] = GracePeriodEndsAt,
        ["RenewalUrl"] = RenewalUrl
    };
}
