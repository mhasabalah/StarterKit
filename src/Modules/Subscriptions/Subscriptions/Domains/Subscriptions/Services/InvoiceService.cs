using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using Shared.Email.Contracts;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

public class InvoiceService(
    SubscriptionsDbContext dbContext,
    ISubscriberInfoProvider subscriberInfoProvider,
    ISender sender,
    IPublisher publisher,
    IConfiguration configuration,
    IOptions<SubscriptionOptions> subscriptionOptions,
    INotificationSender notificationSender,
    ILogger<InvoiceService> logger)
    : IInvoiceService
{
    public async Task<SubscriptionInvoice> GenerateActivationInvoiceAsync(
        Subscription subscription,
        Guid paymentId,
        CancellationToken ct = default)
        => await GenerateInvoiceAsync(subscription, paymentId, InvoiceType.Activation, false, ct);

    public async Task<SubscriptionInvoice> GenerateRenewalInvoiceAsync(
        Subscription subscription,
        Guid paymentId,
        CancellationToken ct = default)
        => await GenerateInvoiceAsync(subscription, paymentId, InvoiceType.Renewal, false, ct);

    public async Task<SubscriptionInvoice> GenerateProratedInvoiceAsync(
        Subscription subscription,
        Guid? paymentId,
        decimal amount,
        bool isDeferred,
        Guid originalPlanId,
        CancellationToken ct = default)
        => await GenerateInvoiceAsync(subscription, paymentId, InvoiceType.Proration, isDeferred, ct, amount, originalPlanId);

    public async Task RetryAsync(
        SubscriptionInvoice invoice,
        Subscription subscription,
        CancellationToken ct = default)
    {
        invoice.Retry();
        await TryGeneratePdfAndSendAsync(invoice, subscription, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task ResendAsync(
        SubscriptionInvoice invoice,
        Subscription subscription,
        CancellationToken ct = default)
    {
        try
        {
            var subscriber = await subscriberInfoProvider.GetAsync(subscription.SubscriberId, ct);
            await DispatchInvoiceEmailAsync(invoice, subscription, subscriber, null, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resend of invoice {InvoiceNumber} failed.", invoice.InvoiceNumber);
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private async Task<SubscriptionInvoice> GenerateInvoiceAsync(
        Subscription subscription,
        Guid? paymentId,
        InvoiceType type,
        bool isDeferred,
        CancellationToken ct,
        decimal? overrideAmount = null,
        Guid? originalPlanId = null)
    {
        var invoiceNumber = await GenerateInvoiceNumberAsync(ct);
        var amount = overrideAmount ?? subscription.Price;
        var taxRate = subscriptionOptions.Value.TaxRate;

        var invoice = SubscriptionInvoice.Create(
            subscription.Id,
            paymentId,
            invoiceNumber,
            type,
            amount,
            subscription.Currency,
            isDeferred,
            originalPlanId,
            taxRate);

        await dbContext.Invoices.AddAsync(invoice, ct);
        subscription.AddInvoice(invoice);

        await TryGeneratePdfAndSendAsync(invoice, subscription, ct);
        await dbContext.SaveChangesAsync(ct);

        return invoice;
    }

    private async Task TryGeneratePdfAndSendAsync(
        SubscriptionInvoice invoice,
        Subscription subscription,
        CancellationToken ct)
    {
        var subscriber = await subscriberInfoProvider.GetAsync(subscription.SubscriberId, ct);

        byte[]? pdfBytes = null;

        try
        {
            if (invoice.PdfStorageKey is null)
            {
                var pdfModel = InvoicePdfModel.From(invoice, subscription, subscriber, subscriptionOptions.Value.Invoice);
                pdfBytes = new InvoiceDocument(pdfModel).GeneratePdf();
                var attachmentId = await UploadInvoicePdfAsync(invoice, pdfBytes, ct);
                invoice.MarkGenerated(attachmentId.ToString());

                logger.LogInformation(
                    "Invoice {InvoiceNumber} PDF generated and stored. AttachmentId={AttachmentId}",
                    invoice.InvoiceNumber, attachmentId);
            }

            await DispatchInvoiceEmailAsync(invoice, subscription, subscriber, pdfBytes, ct);
            invoice.MarkSent();

            await publisher.Publish(new InvoiceGeneratedEvent(
                invoice.Id,
                invoice.SubscriptionId,
                subscription.SubscriberId,
                invoice.Type.ToString(),
                invoice.Amount), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to generate/send invoice {InvoiceNumber} for subscription {SubscriptionId}. Attempt {RetryCount}",
                invoice.InvoiceNumber, invoice.SubscriptionId, invoice.RetryCount + 1);

            invoice.MarkFailed(ex.Message);
            await publisher.Publish(new InvoiceGenerationFailedEvent(
                invoice.Id,
                invoice.SubscriptionId,
                subscription.SubscriberId,
                ex.Message,
                invoice.RetryCount), ct);
        }
    }

    private async Task DispatchInvoiceEmailAsync(
        SubscriptionInvoice invoice,
        Subscription subscription,
        SubscriberInfo? subscriber,
        byte[]? pdfBytes,
        CancellationToken ct)
    {
        var frontendBaseUrl = configuration["Application:FrontendBaseUrl"] ?? "http://localhost:3000";
        var model = new InvoiceIssuedModel
        {
            SubscriberName = subscriber?.DisplayName ?? string.Empty,
            InvoiceNumber = invoice.InvoiceNumber,
            InvoiceType = invoice.Type.ToString(),
            Amount = invoice.Amount.ToString("F2"),
            Currency = invoice.Currency,
            IssuedAt = invoice.IssuedAt.ToString("yyyy-MM-dd"),
            PortalUrl = $"{frontendBaseUrl}/invoices"
        };

        EmailAttachment[]? attachments = pdfBytes is not null
            ? [new EmailAttachment($"{invoice.InvoiceNumber}.pdf", pdfBytes, "application/pdf")]
            : null;

        await notificationSender
            .For(subscription.SubscriberId)
            .WithEmail(EmailTemplate.InvoiceIssued, model.ToDictionary(), attachments)
            .SendAsync(forcedChannel: NotificationChannel.Email, ct: ct);
    }

    private async Task<Guid> UploadInvoicePdfAsync(
        SubscriptionInvoice invoice,
        byte[] pdfBytes,
        CancellationToken ct)
    {
        var fileName = $"{invoice.InvoiceNumber}.pdf";
        await using var stream = new MemoryStream(pdfBytes);

        var result = await sender.Send(new UploadAttachmentCommand(
            FileName: fileName,
            ContentType: "application/pdf",
            FileSize: pdfBytes.Length,
            FileStream: stream,
            Type: AttachmentType.Document,
            EntityType: EntityType.Other,
            EntityId: invoice.SubscriptionId,
            Description: $"Invoice {invoice.InvoiceNumber}",
            IsPublic: false,
            GenerateThumbnail: false), ct);

        return result.Id;
    }

    private async Task<string> GenerateInvoiceNumberAsync(CancellationToken ct)
    {
        var connection = dbContext.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
            await connection.OpenAsync(ct);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT nextval('subscriptions.invoice_number_seq')";
            var seq = (long)(await cmd.ExecuteScalarAsync(ct))!;
            var ym = DateTimeOffset.UtcNow.ToString("yyyyMM");
            var prefix = subscriptionOptions.Value.Invoice.NumberPrefix;
            return $"{prefix}-{ym}-{seq:D5}";
        }
        finally
        {
            if (!wasOpen)
                await connection.CloseAsync();
        }
    }
}
