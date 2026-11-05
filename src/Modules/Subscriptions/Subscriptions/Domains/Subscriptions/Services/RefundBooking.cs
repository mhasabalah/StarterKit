namespace Subscriptions.Domains.Subscriptions.Services;

/// <param name="Notes">The credit notes issued (tracked, delivery not yet queued).</param>
/// <param name="Uncredited">Refunded money no invoice could still absorb.</param>
internal sealed record CreditNoteAllocation(IReadOnlyList<SubscriptionInvoice> Notes, decimal Uncredited);

/// <summary>
/// The credit-note half of booking a refund, shared by the admin refund flow and provider refund webhooks: spreads the
/// refunded amount over the subscription's invoices (newest first, each up to its creditable remainder).
/// </summary>
internal interface IRefundBooking
{
    Task<CreditNoteAllocation> IssueCreditNotesAsync(
        Guid subscriptionId, decimal amount, string reason, CancellationToken ct);
}

internal sealed class RefundBooking(SubscriptionsDbContext dbContext, IInvoiceIssuer invoiceIssuer) : IRefundBooking
{
    public async Task<CreditNoteAllocation> IssueCreditNotesAsync(
        Guid subscriptionId, decimal amount, string reason, CancellationToken ct)
    {
        var invoices = await dbContext.Invoices
            .Where(x => x.SubscriptionId == subscriptionId &&
                        x.Type != InvoiceType.CreditNote &&
                        x.CreditedAmount < x.TotalAmount)
            .OrderByDescending(x => x.IssuedAt)
            .ToListAsync(ct);

        var notes = new List<SubscriptionInvoice>();
        var remaining = amount;
        foreach (var invoice in invoices)
        {
            if (remaining <= 0m)
                break;

            var creditable = Math.Min(remaining, CreditNotePolicy.Creditable(invoice.TotalAmount, invoice.CreditedAmount));
            if (creditable <= 0m)
                continue;

            notes.Add(await invoiceIssuer.IssueCreditNoteAsync(invoice, creditable, reason, ct));
            remaining -= creditable;
        }

        return new CreditNoteAllocation(notes, Math.Max(0m, remaining));
    }
}
