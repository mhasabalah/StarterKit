using Shared.Contracts.Attributes;

namespace Subscriptions.Contracts.Subscriptions.Enums;

/// <summary>How many billing periods a redeemed coupon discounts.</summary>
public enum CouponDuration
{
    [LocalizedDescription("Once", "مرة واحدة")]
    Once = 0,

    [LocalizedDescription("Repeating", "متكرر")]
    Repeating = 1,

    [LocalizedDescription("Forever", "دائمًا")]
    Forever = 2
}
