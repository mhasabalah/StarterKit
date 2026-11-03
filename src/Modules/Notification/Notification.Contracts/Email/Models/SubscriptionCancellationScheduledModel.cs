namespace Notification.Contracts.Email.Models;

public sealed class SubscriptionCancellationScheduledModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string CancellationDate { get; set; } = string.Empty;
    public string SupportWhatsApp { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["CancellationDate"] = CancellationDate,
        ["SupportWhatsApp"] = SupportWhatsApp,
        ["SupportEmail"] = SupportEmail
    };
}
