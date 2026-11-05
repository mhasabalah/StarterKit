namespace Subscriptions.Domains.Metering.Jobs;

/// <summary>Recurring job (<c>flush-usage-counters</c>): copies the Redis usage counters to <c>usage_records</c>.</summary>
[DisableConcurrentExecution(300)]
public class FlushUsageCountersJob(IUsageFlusher flusher, ILogger<FlushUsageCountersJob> logger)
{
    private const int BatchSize = 500;

    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var written = await flusher.FlushAsync(BatchSize, CancellationToken.None);
        if (written > 0)
            logger.LogInformation("FlushUsageCountersJob: {Count} usage counters persisted.", written);
    }
}
