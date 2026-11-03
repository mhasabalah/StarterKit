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

        // Ownership/admin checked above, so ask Attachment for a fresh short-lived signed URL (trusted query).
        // The invoice may have been uploaded by another user (admin/system), so the per-user
        // GetAttachmentByIdQuery access check is not the right gate here.
        var readUrl = await sender.Send(new GetAttachmentReadUrlQuery(attachmentId), cancellationToken);
        return (readUrl.Url, "application/pdf", $"{invoiceData.InvoiceNumber}.pdf");
    }
}
