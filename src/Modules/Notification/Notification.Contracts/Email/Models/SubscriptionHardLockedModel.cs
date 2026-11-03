namespace Notification.Contracts.Email.Models;

/// <summary>Pure data bag; subject and template file live in the Notification module.</summary>
public sealed class SubscriptionHardLockedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["SupportEmail"] = SupportEmail
    };
}
