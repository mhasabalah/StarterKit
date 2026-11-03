namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// Admin issues a credit note against an issued invoice. Issued invoices are immutable: a credit note is a new
/// document (own number from the same gapless sequence, negative amounts) linked to the original invoice.
/// </summary>
/// <param name="Amount">Gross amount to credit (positive). Null credits everything that is still creditable.</param>
/// <param name="Version">The ORIGINAL invoice version the admin saw (<c>AdminInvoiceDto.Version</c>).</param>
public record IssueCreditNoteCommand(
    Guid InvoiceId,
    Guid AdminId,
    string Reason,
    decimal? Amount = null,
    long? Version = null) : ICommand<IssueCreditNoteResult>;

public record IssueCreditNoteResult(
    Guid CreditNoteId,
    string CreditNoteNumber,
    Guid OriginalInvoiceId,
    decimal TotalAmount,
    string Currency);
