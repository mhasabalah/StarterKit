using Hangfire;
using Microsoft.Extensions.Hosting;

namespace Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Bootstrap service to register recurring Hangfire jobs for Identity module
/// </summary>
public class IdentityJobSchedulerBootstrap : IHostedService
{
    private readonly ILogger<IdentityJobSchedulerBootstrap> _logger;

    public IdentityJobSchedulerBootstrap(ILogger<IdentityJobSchedulerBootstrap> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Identity JobScheduler - scheduling recurring jobs");

        // Schedule refresh token cleanup job (runs daily at 2 AM)
        RecurringJob.AddOrUpdate<RefreshTokenCleanupJob>(
            "cleanup-expired-refresh-tokens",
            job => job.ExecuteAsync(),
            Cron.Daily(2)); // Run at 2 AM every day

        _logger.LogInformation(
            "Scheduled recurring job: cleanup-expired-refresh-tokens (runs daily at 2 AM)");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Identity JobScheduler");
        return Task.CompletedTask;
    }
}
