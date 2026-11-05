using Shared.Contracts.Attributes;

namespace Subscriptions.Contracts.Subscriptions.Enums;

/// <summary>When a metered plan feature's usage counter starts again from zero.</summary>
public enum QuotaResetPeriod
{
    [LocalizedDescription("Monthly", "شهريًا")]
    Monthly = 0,

    [LocalizedDescription("Never", "أبدًا")]
    Never = 1
}
