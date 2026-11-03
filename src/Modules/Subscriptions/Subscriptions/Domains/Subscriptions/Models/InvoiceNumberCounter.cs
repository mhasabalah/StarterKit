namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>
/// Per-year gapless invoice number counter (<c>invoice_number_counters(year, last_value)</c>). Incremented with
/// <c>SELECT ... FOR UPDATE</c> inside the same transaction that inserts the invoice, so a rolled-back issue also
/// rolls back the number (unlike a sequence, which leaves gaps).
/// </summary>
public class InvoiceNumberCounter
{
    public int Year { get; private set; }
    public long LastValue { get; private set; }

    private InvoiceNumberCounter()
    {
    }
}
