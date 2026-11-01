using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.Subscriptions.Features.GetInvoicePdf;

public class GetInvoicePdfHandler(SubscriptionsDbContext dbContext, ISender sender)
    : IQueryHandler<GetInvoicePdfQuery, (string FileUrl, string ContentType, string FileName)>
{
    public async Task<(string FileUrl, string ContentType, string FileName)> Handle(
        GetInvoicePdfQuery query,
        CancellationToken cancellationToken)
    {
        var invoiceData = await dbContext.Invoices
            .AsNoTracking()
            .Where(i => i.Id == query.InvoiceId)
            .Select(i => new
            {
                i.Id,
                i.PdfStorageKey,
                i.InvoiceNumber,
                i.Subscription.SubscriberId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invoiceData is null)
            throw new NotFoundException("Invoice not found.");

        if (!query.IsAdmin && invoiceData.SubscriberId != query.ActorId)
            throw new ForbiddenException("You are not allowed to access this invoice.");

        if (string.IsNullOrEmpty(invoiceData.PdfStorageKey) ||
            !Guid.TryParse(invoiceData.PdfStorageKey, out var attachmentId))
        {
            throw new NotFoundException("Invoice PDF has not been generated yet.");
        }

        var attachment = await sender.Send(new GetAttachmentByIdQuery(attachmentId), cancellationToken);
        return (attachment.Attachment.FileUrl, "application/pdf", $"{invoiceData.InvoiceNumber}.pdf");
    }
}
