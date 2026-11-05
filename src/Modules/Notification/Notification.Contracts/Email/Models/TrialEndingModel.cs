namespace Notification.Contracts.Email.Models;

/// <summary>Sent a few days before a free trial ends, with what happens next.</summary>
public sealed class TrialEndingModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string TrialEndDate { get; set; } = string.Empty;
    public int DaysLeft { get; set; }

    /// <summary>What the first paid period costs, e.g. <c>115.00 SAR</c>.</summary>
    public string PriceAfterTrial { get; set; } = string.Empty;

    public string SubscriptionUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["TrialEndDate"] = TrialEndDate,
        ["DaysLeft"] = DaysLeft,
        ["PriceAfterTrial"] = PriceAfterTrial,
        ["SubscriptionUrl"] = SubscriptionUrl
    };
}
