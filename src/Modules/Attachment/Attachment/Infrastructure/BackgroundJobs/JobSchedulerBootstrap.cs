using Hangfire;
using Microsoft.Extensions.Hosting;

namespace Attachment.Infrastructure.BackgroundJobs;

/// <summary>
/// Hosted service that schedules recurring background jobs on application startup
/// </summary>
public class JobSchedulerBootstrap : IHostedService
{
    private readonly ILogger<JobSchedulerBootstrap> _logger;

    public JobSchedulerBootstrap(ILogger<JobSchedulerBootstrap> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting JobSchedulerBootstrap - scheduling recurring jobs");

        // Schedule recurring cleanup job (every 6 hours)
        RecurringJob.AddOrUpdate<CleanupExpiredPendingAttachmentsJob>(
            "cleanup-expired-attachments",
            job => job.ExecuteAsync(),
            "0 */6 * * *", // Cron: At minute 0 past every 6th hour
            new RecurringJobOptions
            {
                TimeZone = TimeZoneInfo.Utc
            });

        _logger.LogInformation("Scheduled recurring job: cleanup-expired-attachments (runs every 6 hours)");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping JobSchedulerBootstrap");
        return Task.CompletedTask;
    }
}
