using Shared.Core.Attributes;

namespace Subscriptions.Contracts.Subscriptions.Enums;

public enum SubscriptionStatus
{
    [LocalizedDescription("Active", "نشط")]
    Active = 1,

    [LocalizedDescription("Past Due", "متأخر الدفع")]
    PastDue = 2,

    [LocalizedDescription("Cancelled", "ملغى")]
    Cancelled = 3,

    [LocalizedDescription("Expired", "منتهي الصلاحية")]
    Expired = 4
}
