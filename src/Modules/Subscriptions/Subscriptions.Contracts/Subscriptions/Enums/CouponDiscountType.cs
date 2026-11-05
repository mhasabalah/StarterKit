using Shared.Contracts.Attributes;

namespace Subscriptions.Contracts.Subscriptions.Enums;

public enum CouponDiscountType
{
    [LocalizedDescription("Percent", "نسبة مئوية")]
    Percent = 0,

    [LocalizedDescription("Fixed amount", "مبلغ ثابت")]
    FixedAmount = 1
}
