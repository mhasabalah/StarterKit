namespace Notification.Contracts.Email.Models;

/// <summary>Daily billing digest for the billing team (Subscriptions <c>admin-billing-digest</c> job).</summary>
public sealed class AdminBillingDigestModel
{
    public string PendingProofs { get; set; } = string.Empty;
    public string StaleProofs { get; set; } = string.Empty;
    public string StaleThresholdHours { get; set; } = string.Empty;
    public string OldestPendingAge { get; set; } = string.Empty;
    public string PastDueCount { get; set; } = string.Empty;
    public string PartiallyPaidCount { get; set; } = string.Empty;
    public string GeneratedAt { get; set; } = string.Empty;
    public string AdminDashboardUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["PendingProofs"] = PendingProofs,
        ["StaleProofs"] = StaleProofs,
        ["StaleThresholdHours"] = StaleThresholdHours,
        ["OldestPendingAge"] = OldestPendingAge,
        ["PastDueCount"] = PastDueCount,
        ["PartiallyPaidCount"] = PartiallyPaidCount,
        ["GeneratedAt"] = GeneratedAt,
        ["AdminDashboardUrl"] = AdminDashboardUrl
    };
}
