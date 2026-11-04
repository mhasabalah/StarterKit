using Subscriptions.Domains.OnlinePayments.Models;
using Subscriptions.Domains.OnlinePayments.Services;
using Subscriptions.Options;

namespace Subscriptions.Domains.OnlinePayments.Jobs;

/// <summary>
/// Retries payment webhooks whose processing failed transiently (and Pending rows abandoned for a few minutes) (job id <c>retry-failed-payment-webhooks</c>).
/// Bounded by <c>Subscriptions:PaymentWebhooks:MaxAttempts</c>, with a linear backoff; permanent rejections have their
/// attempts exhausted at once and are left for an admin. One <see cref="IUnitOfWorkRunner"/> unit of work per event,
/// so a failing event never blocks the others and its domain events are published after its own commit.
/// </summary>
[DisableConcurrentExecution(60)]
public class RetryFailedPaymentWebhooksJob(
    IUnitOfWorkRunner unitOfWork,
    IOptions<SubscriptionOptions> options,
    TimeProvider timeProvider,
    ILogger<RetryFailedPaymentWebhooksJob> logger)
{
    private const int BatchSize = 50;

    /// <summary>A Pending row older than this was abandoned (crash / cancelled request between insert and processing).</summary>
    private static readonly TimeSpan PendingGrace = TimeSpan.FromMinutes(5);

    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = options.Value.PaymentWebhooks;
        var now = timeProvider.GetUtcNow();

        var candidates = await unitOfWork.RunAsync(async (services, ct) =>
        {
            var dbContext = services.GetRequiredService<SubscriptionsDbContext>();
            var failed = await dbContext.InboundWebhookEvents
                .AsNoTracking()
                .Where(x => (x.Status == InboundWebhookStatus.Failed && x.Attempts < settings.MaxAttempts) ||
                            (x.Status == InboundWebhookStatus.Pending && x.ReceivedAt <= now - PendingGrace))
                .Select(x => new { x.Id, x.Attempts, x.LastAttemptAt })
                .ToListAsync(ct);

            return failed
                .Where(x => x.LastAttemptAt is null ||
                            x.LastAttemptAt.Value.AddMinutes(x.Attempts * settings.RetryBackoffMinutes) <= now)
                .OrderBy(x => x.LastAttemptAt)
                .Take(BatchSize)
                .Select(x => x.Id)
                .ToList();
        }, CancellationToken.None);

        foreach (var id in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await unitOfWork.RunAsync(
                (services, ct) => services.GetRequiredService<IInboundWebhookProcessor>().ProcessAsync(id, ct),
                CancellationToken.None);
        }

        if (candidates.Count > 0)
        {
            logger.LogInformation("Retried {Count} failed payment webhooks.", candidates.Count);
        }
    }
}
