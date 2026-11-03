using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>
/// Hourly expiry scheduler (job id <c>process-expired</c>): expires PastDue subscriptions whose grace period ended,
/// except those with a renewal proof under review. Logic lives in <see cref="IRenewalOrchestrator.ProcessExpiriesAsync"/>.
/// </summary>
[DisableConcurrentExecution(60)]
public class ProcessExpiredSubscriptionsJob(
    IRenewalOrchestrator orchestrator,
    TimeProvider timeProvider,
    ILogger<ProcessExpiredSubscriptionsJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expired = await orchestrator.ProcessExpiriesAsync(timeProvider.GetUtcNow(), CancellationToken.None);
        if (expired > 0)
        {
            logger.LogInformation("Expired {Count} subscriptions.", expired);
        }
    }
}
