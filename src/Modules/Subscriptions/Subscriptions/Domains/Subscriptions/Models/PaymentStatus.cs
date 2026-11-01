namespace Subscriptions.Domains.Subscriptions.Models;

public enum PaymentStatus
{
    [LocalizedDescription("Pending", "قيد الانتظار")]
    Pending = 0,

    [LocalizedDescription("Submitted", "تم الإرسال")]
    Submitted = 1,

    [LocalizedDescription("Paid", "مدفوع")]
    Paid = 2,

    [LocalizedDescription("Failed", "فشل")]
    Failed = 3,

    [LocalizedDescription("Rejected", "مرفوض")]
    Rejected = 4
}
