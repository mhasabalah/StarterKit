using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.IssueCreditNote;

public class IssueCreditNoteCommandValidator : AbstractValidator<IssueCreditNoteCommand>
{
    public IssueCreditNoteCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(400);
        RuleFor(x => x.Amount).GreaterThan(0m).When(x => x.Amount.HasValue);
    }
}

/// <summary>
/// Credits an issued invoice (fully or partially, up to its creditable remainder). The original stays immutable;
/// only its credited-amount bookkeeping moves (protected by the version check, so concurrent notes cannot over-credit).
/// </summary>
public class IssueCreditNoteHandler(
    SubscriptionsDbContext dbContext,
    IInvoiceIssuer invoiceIssuer,
    ILedgerRepository ledger,
    IAdminAuditWriter audit)
    : ICommandHandler<IssueCreditNoteCommand, IssueCreditNoteResult>
{
    public async Task<IssueCreditNoteResult> Handle(IssueCreditNoteCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var original = await dbContext.Invoices
            .FirstOrDefaultAsync(x => x.Id == command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException($"Invoice {command.InvoiceId} not found.");

        dbContext.ExpectVersion(original, command.Version);
        var creditedBefore = original.CreditedAmount;

        var note = await invoiceIssuer.IssueCreditNoteAsync(original, command.Amount, command.Reason, cancellationToken);
        ledger.Add(LedgerEntry.ForCreditNote(note, command.AdminId, DateTimeOffset.UtcNow));

        audit.Record(
            "IssueCreditNote",
            original.Id,
            "Invoice",
            original.SubscriberId,
            command.Reason,
            note.TotalAmount,
            note.Currency,
            before: new { original.InvoiceNumber, CreditedAmount = creditedBefore },
            after: new { CreditedAmount = original.CreditedAmount, CreditNoteId = note.Id, CreditNoteNumber = note.InvoiceNumber },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        invoiceIssuer.EnqueueDelivery(note.Id);

        return new IssueCreditNoteResult(note.Id, note.InvoiceNumber, original.Id, note.TotalAmount, note.Currency);
    }
}
