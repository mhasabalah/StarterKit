using Shared.Contracts.Attributes;

namespace Subscriptions.Contracts.Subscriptions.Enums;

/// <summary>
/// The single subscription lifecycle status, shared by the domain, the database and other modules.
/// Numeric values are explicit and stable (never renumber them): the database stores the NAME (string column),
/// but API clients and other modules may rely on the numbers.
/// Open statuses (at most one per subscriber, enforced by a filtered unique index): PendingApproval, Trialing, Active, PastDue.
/// </summary>
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
    Rejected = 5,

    /// <summary>Free trial: full access, no payment yet. Appended (value 6): existing numbers never change.</summary>
    [LocalizedDescription("Trialing", "فترة تجريبية")]
    Trialing = 6
}
