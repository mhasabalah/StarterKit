using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>
/// Backstop for invoice delivery (job id <c>process-invoice-retry</c>): re-enqueues invoices whose delivery was
/// never queued (process stopped between commit and enqueue) or failed with retries left.
/// Delivery itself is idempotent (<see cref="InvoiceDeliveryJob"/>).
/// </summary>
[DisableConcurrentExecution(60)]
public class ProcessInvoiceRetryJob(
    IUnitOfWorkRunner unitOfWork,
    ILogger<ProcessInvoiceRetryJob> logger)
{
    public const int MaxRetryCount = 5;
    private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(15);

    public Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return unitOfWork.RunAsync((services, _) => ExecuteAsync(services, cancellationToken), CancellationToken.None);
    }

    private async Task ExecuteAsync(IServiceProvider services, IJobCancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
        var issuer = services.GetRequiredService<IInvoiceIssuer>();

        var stuckBefore = DateTimeOffset.UtcNow - StuckAfter;
        var invoiceIds = await dbContext.Invoices
            .AsNoTracking()
            .Where(x => x.SentAt == null &&
                        ((x.Status == InvoiceStatus.Pending && x.CreatedAt <= stuckBefore) ||
                         (x.Status == InvoiceStatus.Generated && x.CreatedAt <= stuckBefore) ||
                         (x.Status == InvoiceStatus.Failed && x.RetryCount < MaxRetryCount)))
            .Select(x => x.Id)
            .ToListAsync(CancellationToken.None);

        foreach (var invoiceId in invoiceIds)
        {
            issuer.EnqueueDelivery(invoiceId);
        }

        if (invoiceIds.Count > 0)
        {
            logger.LogInformation("Re-enqueued delivery of {Count} invoices.", invoiceIds.Count);
        }
    }
}
