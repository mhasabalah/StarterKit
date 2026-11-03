using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>
/// Hourly renewal-boundary scheduler (job id <c>process-past-due</c>). Thin: all boundary logic — scheduled
/// cancellation, then scheduled downgrade, then past-due + renewal payment — lives in <see cref="IRenewalOrchestrator"/>.
/// It replaces the former separate cancellation/downgrade jobs that raced each other at the same boundary.
/// </summary>
[DisableConcurrentExecution(60)]
public class ProcessPastDueSubscriptionsJob(
    IRenewalOrchestrator orchestrator,
    TimeProvider timeProvider,
    ILogger<ProcessPastDueSubscriptionsJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var outcomes = await orchestrator.ProcessDueRenewalsAsync(timeProvider.GetUtcNow(), CancellationToken.None);

        if (outcomes.Count > 0)
        {
            logger.LogInformation(
                "Renewal boundary processed {Count} subscriptions: {Billed} billed, {Cancelled} cancelled, {Skipped} skipped.",
                outcomes.Count,
                outcomes.Values.Count(x => x is RenewalBoundaryOutcome.Billed or RenewalBoundaryOutcome.BilledAfterDowngrade),
                outcomes.Values.Count(x => x == RenewalBoundaryOutcome.Cancelled),
                outcomes.Values.Count(x => x == RenewalBoundaryOutcome.Skipped));
        }
    }
}
