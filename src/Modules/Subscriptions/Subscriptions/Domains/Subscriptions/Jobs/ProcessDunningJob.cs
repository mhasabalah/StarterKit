using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>Recurring job (<c>process-dunning</c>): overdue-renewal reminders. Logic: <see cref="IDunningProcessor"/>.</summary>
[DisableConcurrentExecution(60)]
public class ProcessDunningJob(
    IDunningProcessor processor,
    TimeProvider timeProvider,
    ILogger<ProcessDunningJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sent = await processor.ProcessAsync(timeProvider.GetUtcNow(), CancellationToken.None);
        if (sent > 0)
            logger.LogInformation("ProcessDunningJob: {Count} dunning notices sent.", sent);
    }
}
