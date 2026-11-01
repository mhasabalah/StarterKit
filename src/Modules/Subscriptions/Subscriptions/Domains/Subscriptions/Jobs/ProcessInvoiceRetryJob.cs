using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Jobs;

[DisableConcurrentExecution(60)]
public class ProcessInvoiceRetryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessInvoiceRetryJob> logger)
{
    private const int MaxRetryCount = 3;

    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SubscriptionsDbContext>();
        var invoiceService = scope.ServiceProvider.GetRequiredService<IInvoiceService>();

        var pendingInvoiceIds = await dbContext.Invoices
            .AsNoTracking()
            .Where(x => (x.Status == InvoiceStatus.Pending || x.Status == InvoiceStatus.Failed)
                        && !x.IsVoided
                        && x.RetryCount < MaxRetryCount)
            .Select(x => x.Id)
            .ToListAsync(CancellationToken.None);

        foreach (var invoiceId in pendingInvoiceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var invoice = await dbContext.Invoices
                    .FirstOrDefaultAsync(x => x.Id == invoiceId, CancellationToken.None);
                if (invoice is null)
                    continue;

                var subscription = await dbContext.Subscriptions
                    .Include(x => x.Invoices)
                    .FirstOrDefaultAsync(x => x.Id == invoice.SubscriptionId, CancellationToken.None);
                if (subscription is null)
                    continue;

                await invoiceService.RetryAsync(invoice, subscription, CancellationToken.None);

                logger.LogInformation(
                    "Retried invoice {InvoiceId} ({InvoiceNumber}). Status={Status} RetryCount={RetryCount}",
                    invoice.Id, invoice.InvoiceNumber, invoice.Status, invoice.RetryCount);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrying invoice {InvoiceId}.", invoiceId);
                dbContext.ChangeTracker.Clear();
            }
        }
    }
}
