namespace Subscriptions.Domains.Subscriptions.Models;

public enum InvoiceStatus
{
    [LocalizedDescription("Pending", "قيد الإنشاء")]
    Pending = 0,

    [LocalizedDescription("Generated", "تم الإنشاء")]
    Generated = 1,

    [LocalizedDescription("Sent", "تم الإرسال")]
    Sent = 2,

    [LocalizedDescription("Failed", "فشل")]
    Failed = 3
}
