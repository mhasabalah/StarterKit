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

public class ResendInvoiceHandler(
    SubscriptionsDbContext dbContext,
    IInvoiceService invoiceService)
    : ICommandHandler<ResendInvoiceCommand, Unit>
{
    public async Task<Unit> Handle(ResendInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.Invoices
            .FirstOrDefaultAsync(x => x.Id == command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException($"Invoice {command.InvoiceId} not found.");

        if (invoice.IsVoided)
            throw new ConflictException("Cannot resend a voided invoice.");

        if (invoice.Status == InvoiceStatus.Pending || invoice.Status == InvoiceStatus.Failed)
            throw new ConflictException("Invoice PDF has not been generated yet. Use retry instead.");

        var subscription = await dbContext.Subscriptions
            .Include(x => x.Invoices)
            .FirstOrDefaultAsync(x => x.Id == invoice.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(invoice.SubscriptionId);

        await invoiceService.ResendAsync(invoice, subscription, cancellationToken);
        return Unit.Value;
    }
}
