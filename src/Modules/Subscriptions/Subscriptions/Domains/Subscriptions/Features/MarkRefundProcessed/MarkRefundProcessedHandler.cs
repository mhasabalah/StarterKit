using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.MarkRefundProcessed;

public class MarkRefundProcessedCommandValidator : AbstractValidator<MarkRefundProcessedCommand>
{
    public MarkRefundProcessedCommandValidator()
    {
        RuleFor(x => x.RefundRequestId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
        RuleFor(x => x.ExternalTransactionRef).NotEmpty().MaximumLength(200);
    }
}

/// <summary>
/// Marks an approved refund as paid out and issues credit notes for the refunded amount against the subscription's
/// invoices (newest first, each up to its creditable remainder), and writes the Refund ledger entry, all in one
/// transaction.
/// </summary>
public class MarkRefundProcessedHandler(
    SubscriptionsDbContext dbContext,
    IInvoiceIssuer invoiceIssuer,
    ILedgerRepository ledger,
    IAdminAuditWriter audit)
    : ICommandHandler<MarkRefundProcessedCommand, Unit>
{
    public async Task<Unit> Handle(MarkRefundProcessedCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var refundRequest = await dbContext.RefundRequests
            .FirstOrDefaultAsync(x => x.Id == command.RefundRequestId, cancellationToken)
            ?? throw new NotFoundException($"Refund request {command.RefundRequestId} not found.");

        dbContext.ExpectVersion(refundRequest, command.Version);
        refundRequest.MarkProcessed(command.AdminId, command.ExternalTransactionRef);

        var invoices = await dbContext.Invoices
            .Where(x => x.SubscriptionId == refundRequest.SubscriptionId &&
                        x.Type != InvoiceType.CreditNote &&
                        x.CreditedAmount < x.TotalAmount)
            .OrderByDescending(x => x.IssuedAt)
            .ToListAsync(cancellationToken);

        var notes = new List<SubscriptionInvoice>();
        var remaining = refundRequest.RequestedAmount;
        foreach (var invoice in invoices)
        {
            if (remaining <= 0m)
                break;

            var amount = Math.Min(remaining, CreditNotePolicy.Creditable(invoice.TotalAmount, invoice.CreditedAmount));
            if (amount <= 0m)
                continue;

            notes.Add(await invoiceIssuer.IssueCreditNoteAsync(
                invoice, amount, $"Refund {command.ExternalTransactionRef}", cancellationToken));
            remaining -= amount;
        }

        // Money left: ONE Refund entry. The credit notes above document it and are not booked again in the ledger.
        ledger.Add(LedgerEntry.ForRefund(refundRequest, notes.FirstOrDefault()?.Id, command.AdminId, DateTimeOffset.UtcNow));

        audit.Record(
            "ProcessRefund",
            refundRequest.Id,
            "RefundRequest",
            refundRequest.SubscriberId,
            command.ExternalTransactionRef,
            refundRequest.RequestedAmount,
            refundRequest.Currency,
            before: new { Status = RefundStatus.Approved.ToString() },
            after: new
            {
                Status = refundRequest.Status.ToString(),
                CreditNotes = notes.Select(x => new { x.Id, x.InvoiceNumber, x.TotalAmount }).ToList(),
                UncreditedAmount = remaining
            },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var note in notes)
        {
            invoiceIssuer.EnqueueDelivery(note.Id);
        }

        return Unit.Value;
    }
}
