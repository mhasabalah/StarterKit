namespace Shared.Email.Jobs;

/// <summary>
/// Hangfire job — counts email_log failures in the last 24 hours and logs the result to Seq.
/// Scheduled as a recurring job in UseEmailInfrastructure via IRecurringJobManager
/// (not IHostedService — that pattern is for long-running loops, not one-time startup registration).
/// </summary>
public sealed class EmailDailySummaryJob(
    EmailLogRepository repository,
    ILogger<EmailDailySummaryJob> logger)
{
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        // P3: CountAsync — single COUNT(*) query, no entity materialization.
        var count = await repository.CountFailedSince24HoursAsync(ct);

        if (count == 0)
            logger.LogInformation("Daily email summary: no failures in the last 24 hours.");
        else
            logger.LogWarning("Daily email summary: {FailedCount} email(s) failed in the last 24 hours.", count);
    }
}
