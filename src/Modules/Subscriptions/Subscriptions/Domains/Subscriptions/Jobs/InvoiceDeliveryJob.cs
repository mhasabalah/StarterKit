using QuestPDF.Fluent;
using Subscriptions.Domains.Subscriptions.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>
/// Renders an issued invoice's PDF from its snapshot, stores it and emails it. Enqueued after the issuing transaction
/// commits. Idempotent by invoice id: the PDF is uploaded once (key persisted before the email), the email is sent
/// once unless <c>forceResend</c>. Failures are recorded on the invoice and rethrown so Hangfire retries;
/// <see cref="ProcessInvoiceRetryJob"/> is the backstop for deliveries that were never enqueued.
/// Runs as one <see cref="IUnitOfWorkRunner"/> unit of work, with a dispatch checkpoint after the PDF upload commits
/// so the attachment's events are published even if the email step fails afterwards.
/// </summary>
[AutomaticRetry(Attempts = 3)]
public class InvoiceDeliveryJob(
    IUnitOfWorkRunner unitOfWork,
    ILogger<InvoiceDeliveryJob> logger)
{
    public Task DeliverAsync(Guid invoiceId, bool forceResend) =>
        unitOfWork.RunAsync((services, ct) => DeliverAsync(services, invoiceId, forceResend, ct), CancellationToken.None);

    private async Task DeliverAsync(IServiceProvider services, Guid invoiceId, bool forceResend, CancellationToken ct)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var options = services.GetRequiredService<IOptions<SubscriptionOptions>>().Value;
        var publisher = services.GetRequiredService<IPublisher>();

        var invoice = await dbContext.Invoices.FirstOrDefaultAsync(x => x.Id == invoiceId, ct);
        if (invoice is null)
        {
            logger.LogWarning("Invoice {InvoiceId} not found for delivery.", invoiceId);
            return;
        }

        if (!forceResend && invoice.SentAt is not null)
        {
            return;
        }

        try
        {
            // Rendered from the issue-time snapshot only, so it is identical on every attempt.
            var pdf = new InvoiceDocument(InvoicePdfModel.FromSnapshot(invoice, options.Invoice)).GeneratePdf();

            if (invoice.PdfStorageKey is null)
            {
                var attachmentId = await UploadAsync(services.GetRequiredService<ISender>(), invoice, pdf, ct);
                invoice.MarkGenerated(attachmentId.ToString());
                await dbContext.SaveChangesAsync(ct);

                // Checkpoint: the upload is committed; publish its events now in case a later step fails.
                await services.GetRequiredService<IDomainEventDispatcher>().DispatchPendingAsync(ct);
            }

            var firstSend = invoice.SentAt is null;
            await SendEmailAsync(services, invoice, pdf, ct);
            invoice.MarkSent(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(ct);

            if (firstSend)
            {
                await publisher.Publish(new InvoiceGeneratedEvent(
                    invoice.Id, invoice.SubscriptionId, invoice.SubscriberId, invoice.Type.ToString(), invoice.TotalAmount), ct);
            }

            logger.LogInformation("Invoice {InvoiceNumber} delivered.", invoice.InvoiceNumber);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delivery of invoice {InvoiceNumber} failed (attempt {Attempt}).",
                invoice.InvoiceNumber, invoice.RetryCount + 1);

            dbContext.ChangeTracker.Clear();
            var failed = await dbContext.Invoices.FirstAsync(x => x.Id == invoiceId, ct);
            failed.MarkFailed(ex.Message);
            await dbContext.SaveChangesAsync(ct);

            await publisher.Publish(new InvoiceGenerationFailedEvent(
                failed.Id, failed.SubscriptionId, failed.SubscriberId, ex.Message, failed.RetryCount), ct);
            throw;
        }
    }

    private static async Task<Guid> UploadAsync(ISender sender, SubscriptionInvoice invoice, byte[] pdf, CancellationToken ct)
    {
        await using var stream = new MemoryStream(pdf);
        var result = await sender.Send(new UploadAttachmentCommand(
            FileName: $"{invoice.InvoiceNumber}.pdf",
            ContentType: "application/pdf",
            FileSize: pdf.Length,
            FileStream: stream,
            Type: AttachmentType.Document,
            EntityType: EntityType.Other,
            EntityId: invoice.SubscriptionId,
            Description: $"Invoice {invoice.InvoiceNumber}",
            IsPublic: false,
            GenerateThumbnail: false), ct);

        return result.Id;
    }

    private static async Task SendEmailAsync(IServiceProvider services, SubscriptionInvoice invoice, byte[] pdf, CancellationToken ct)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var notificationSender = services.GetRequiredService<INotificationSender>();
        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";

        var model = new InvoiceIssuedModel
        {
            SubscriberName = invoice.BuyerName,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceType = invoice.IsCreditNote ? "Credit Note" : invoice.Type.ToString(),
            Amount = invoice.TotalAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            Currency = invoice.Currency,
            IssuedAt = invoice.IssuedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            PortalUrl = $"{frontendBaseUrl}/invoices"
        };

        await notificationSender
            .For(invoice.SubscriberId)
            .WithEmail(EmailTemplate.InvoiceIssued, model.ToDictionary(),
                [new EmailAttachment($"{invoice.InvoiceNumber}.pdf", pdf, "application/pdf")])
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: ct);
    }
}
