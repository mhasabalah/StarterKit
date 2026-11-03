using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.AdminInvoices;

public class ResendInvoiceCommandValidator : AbstractValidator<ResendInvoiceCommand>
{
    public ResendInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
    }
}

/// <summary>Emails an already delivered invoice again (PDF re-rendered from the snapshot).</summary>
public class ResendInvoiceHandler(
    SubscriptionsDbContext dbContext,
    IInvoiceIssuer invoiceIssuer,
    IAdminAuditWriter audit)
    : ICommandHandler<ResendInvoiceCommand, Unit>
{
    public async Task<Unit> Handle(ResendInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException($"Invoice {command.InvoiceId} not found.");

        if (invoice.PdfStorageKey is null)
            throw new ConflictException("Invoice PDF has not been generated yet. Use retry instead.");

        audit.Record(
            "ResendInvoice",
            invoice.Id,
            "Invoice",
            invoice.SubscriberId,
            amount: invoice.TotalAmount,
            currency: invoice.Currency,
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        invoiceIssuer.EnqueueDelivery(invoice.Id, forceResend: true);
        return Unit.Value;
    }
}
