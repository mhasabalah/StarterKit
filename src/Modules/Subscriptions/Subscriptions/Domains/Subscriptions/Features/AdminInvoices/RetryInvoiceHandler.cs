using Subscriptions.Contracts.Subscriptions.Commands;
using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.AdminInvoices;

public class RetryInvoiceCommandValidator : AbstractValidator<RetryInvoiceCommand>
{
    public RetryInvoiceCommandValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.AdminId).NotEmpty();
    }
}

/// <summary>Re-queues PDF generation + email for an invoice that has not been delivered yet.</summary>
public class RetryInvoiceHandler(
    SubscriptionsDbContext dbContext,
    IInvoiceIssuer invoiceIssuer,
    IAdminAuditWriter audit)
    : ICommandHandler<RetryInvoiceCommand, Unit>
{
    public async Task<Unit> Handle(RetryInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.Invoices
            .FirstOrDefaultAsync(x => x.Id == command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException($"Invoice {command.InvoiceId} not found.");

        if (invoice.Status == InvoiceStatus.Sent)
            throw new ConflictException("Invoice has already been sent successfully. Use resend instead.");

        var before = invoice.Status;
        invoice.ResetForRetry();

        audit.Record(
            "RetryInvoice",
            invoice.Id,
            "Invoice",
            invoice.SubscriberId,
            amount: invoice.TotalAmount,
            currency: invoice.Currency,
            before: new { Status = before.ToString(), invoice.RetryCount },
            after: new { Status = invoice.Status.ToString() },
            adminId: command.AdminId);

        await dbContext.SaveChangesAsync(cancellationToken);
        invoiceIssuer.EnqueueDelivery(invoice.Id);
        return Unit.Value;
    }
}
