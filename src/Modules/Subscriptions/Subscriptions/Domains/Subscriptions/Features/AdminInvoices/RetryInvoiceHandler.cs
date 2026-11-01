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

public class RetryInvoiceHandler(
    SubscriptionsDbContext dbContext,
    IInvoiceService invoiceService)
    : ICommandHandler<RetryInvoiceCommand, Unit>
{
    public async Task<Unit> Handle(RetryInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await dbContext.Invoices
            .FirstOrDefaultAsync(x => x.Id == command.InvoiceId, cancellationToken)
            ?? throw new NotFoundException($"Invoice {command.InvoiceId} not found.");

        if (invoice.IsVoided)
            throw new ConflictException("Cannot retry a voided invoice.");

        if (invoice.Status == InvoiceStatus.Sent)
            throw new ConflictException("Invoice has already been sent successfully.");

        var subscription = await dbContext.Subscriptions
            .Include(x => x.Invoices)
            .FirstOrDefaultAsync(x => x.Id == invoice.SubscriptionId, cancellationToken)
            ?? throw new SubscriptionNotFoundException(invoice.SubscriptionId);

        await invoiceService.RetryAsync(invoice, subscription, cancellationToken);
        return Unit.Value;
    }
}
