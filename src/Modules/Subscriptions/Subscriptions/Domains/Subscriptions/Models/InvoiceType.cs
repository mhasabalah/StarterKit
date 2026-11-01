namespace Subscriptions.Domains.Subscriptions.Models;

public enum InvoiceType
{
    [LocalizedDescription("Activation", "تفعيل")]
    Activation = 0,

    [LocalizedDescription("Renewal", "تجديد")]
    Renewal = 1,

    [LocalizedDescription("Proration", "تسوية جزئية")]
    Proration = 2
}
