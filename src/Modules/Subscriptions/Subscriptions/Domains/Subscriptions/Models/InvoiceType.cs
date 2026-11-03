namespace Subscriptions.Domains.Subscriptions.Models;

public enum InvoiceType
{
    [LocalizedDescription("Activation", "تفعيل")]
    Activation = 0,

    [LocalizedDescription("Renewal", "تجديد")]
    Renewal = 1,

    [LocalizedDescription("Proration", "تسوية جزئية")]
    Proration = 2,

    /// <summary>Negative document linked to an original invoice (refund, upgrade revert, admin correction).</summary>
    [LocalizedDescription("Credit Note", "إشعار دائن")]
    CreditNote = 3
}
