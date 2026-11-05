using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Jobs;

/// <summary>Daily job (<c>process-trial-reminders</c>): "your trial is ending" emails. Logic: <see cref="ITrialReminderProcessor"/>.</summary>
[DisableConcurrentExecution(60)]
public class ProcessTrialRemindersJob(
    ITrialReminderProcessor processor,
    TimeProvider timeProvider,
    ILogger<ProcessTrialRemindersJob> logger)
{
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sent = await processor.ProcessAsync(timeProvider.GetUtcNow(), CancellationToken.None);
        logger.LogInformation("ProcessTrialRemindersJob: {Count} trial-ending reminders sent.", sent);
    }
}
