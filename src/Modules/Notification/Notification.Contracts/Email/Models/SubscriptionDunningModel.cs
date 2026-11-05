namespace Notification.Contracts.Email.Models;

/// <summary>Payment reminder for an overdue renewal (dunning), with a fresh payment link.</summary>
public sealed class SubscriptionDunningModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>The amount still owed, e.g. <c>115.00 SAR</c>.</summary>
    public string AmountDue { get; set; } = string.Empty;

    public string GraceDeadline { get; set; } = string.Empty;

    /// <summary>The hosted checkout when one could be created; otherwise the renewal page.</summary>
    public string PaymentUrl { get; set; } = string.Empty;

    /// <summary>True for the last reminder before the grace period ends.</summary>
    public bool IsFinal { get; set; }

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["PlanName"] = PlanName,
        ["AmountDue"] = AmountDue,
        ["GraceDeadline"] = GraceDeadline,
        ["PaymentUrl"] = PaymentUrl,
        ["IsFinal"] = IsFinal
    };
}
