namespace Subscriptions.Domains.Subscriptions.Models;

public enum PlanChangeType
{
    [LocalizedDescription("Upgrade", "ترقية")]
    Upgrade = 0,

    [LocalizedDescription("Downgrade", "تخفيض")]
    Downgrade = 1
}
