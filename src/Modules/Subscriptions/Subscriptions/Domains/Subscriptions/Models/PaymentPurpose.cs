namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>What a payment pays for. Stored by name.</summary>
public enum PaymentPurpose
{
    [LocalizedDescription("Activation", "تفعيل")]
    Activation = 0,

    [LocalizedDescription("Renewal", "تجديد")]
    Renewal = 1,

    [LocalizedDescription("Proration", "فرق ترقية")]
    Proration = 2
}
