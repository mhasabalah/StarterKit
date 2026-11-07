using Hangfire;
using Identity.Domains.Sso.Jobs;
using Identity.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.BackgroundJobs;

/// <summary>
/// Bootstrap service to register recurring Hangfire jobs for Identity module
/// </summary>
public class IdentityJobSchedulerBootstrap : IHostedService
{
    public const string RefreshSamlMetadataJobId = "refresh-saml-metadata";
    public const string CheckSamlCertificateExpiryJobId = "check-saml-certificate-expiry";

    private readonly ILogger<IdentityJobSchedulerBootstrap> _logger;
    private readonly IOptions<SsoOptions> _sso;

    public IdentityJobSchedulerBootstrap(ILogger<IdentityJobSchedulerBootstrap> logger, IOptions<SsoOptions> sso)
    {
        _logger = logger;
        _sso = sso;
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

        ScheduleSso(_sso.Value);
        return Task.CompletedTask;
    }

    /// <summary>SAML metadata refresh and certificate expiry alerts (cron and switches in <c>Identity:Sso</c>).</summary>
    private void ScheduleSso(SsoOptions options)
    {
        if (options.Enabled && options.MetadataRefreshEnabled)
        {
            RecurringJob.AddOrUpdate<RefreshSamlMetadataJob>(
                RefreshSamlMetadataJobId, job => job.ExecuteAsync(CancellationToken.None), options.MetadataRefreshCron);
            _logger.LogInformation("Scheduled recurring job: {JobId} ({Cron})", RefreshSamlMetadataJobId, options.MetadataRefreshCron);
        }
        else
        {
            RecurringJob.RemoveIfExists(RefreshSamlMetadataJobId);
        }

        if (options.Enabled && options.CertificateExpiryAlertsEnabled)
        {
            RecurringJob.AddOrUpdate<CheckSamlCertificateExpiryJob>(
                CheckSamlCertificateExpiryJobId, job => job.ExecuteAsync(CancellationToken.None), options.CertificateExpiryCheckCron);
            _logger.LogInformation("Scheduled recurring job: {JobId} ({Cron})", CheckSamlCertificateExpiryJobId, options.CertificateExpiryCheckCron);
        }
        else
        {
            RecurringJob.RemoveIfExists(CheckSamlCertificateExpiryJobId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Identity JobScheduler");
        return Task.CompletedTask;
    }
}
