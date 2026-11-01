namespace Subscriptions.Domains.Subscriptions.Models;

public enum SubscriptionStatus
{
    [LocalizedDescription("Pending Approval", "في انتظار الموافقة")]
    PendingApproval = 0,

    [LocalizedDescription("Active", "نشط")]
    Active = 1,

    [LocalizedDescription("Past Due", "متأخر السداد")]
    PastDue = 2,

    [LocalizedDescription("Expired", "منتهي الصلاحية")]
    Expired = 3,

    [LocalizedDescription("Cancelled", "ملغى")]
    Cancelled = 4,

    [LocalizedDescription("Rejected", "مرفوض")]
    Rejected = 5
}
