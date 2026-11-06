using Hangfire;
using Identity.Data.Repository.Sso;
using Identity.Options;
using Microsoft.Extensions.Options;

namespace Identity.Domains.Sso.Jobs;

/// <summary>
/// Daily job: finds SAML connections whose IdP signing certificates are about to expire and raises
/// <c>SsoCertificateExpiringEvent</c> (once per expiry date and threshold, remembered on the connection) so the
/// tenant's SSO administrators can act before sign-in breaks. One unit of work per connection.
/// </summary>
[Queue("default")]
public class CheckSamlCertificateExpiryJob(
    IUnitOfWorkRunner unitOfWork,
    IOptions<SsoOptions> options,
    TimeProvider timeProvider,
    ILogger<CheckSamlCertificateExpiryJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var thresholds = options.Value.CertificateExpiryAlertDays;
        if (!options.Value.Enabled || thresholds.Length == 0)
            return;

        var ids = await unitOfWork.RunAsync(
            (services, token) => services.GetRequiredService<ISsoConnectionRepository>().GetActiveSamlIdsAsync(token), ct);

        var alerted = 0;
        foreach (var id in ids)
        {
            try
            {
                alerted += await unitOfWork.RunAsync(async (services, token) =>
                {
                    var connections = services.GetRequiredService<ISsoConnectionRepository>();
                    var connection = await connections.GetByIdAsync(id, token);
                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    if (connection?.ExpiryAlertDue(now, thresholds) is not { } threshold)
                        return 0;

                    connection.MarkExpiryAlerted(threshold, now);
                    await connections.SaveAsync(token);
                    return 1;
                }, ct);
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict())
            {
                logger.LogInformation("SSO certificate expiry check of connection {ConnectionId} lost a concurrency race", id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "SSO certificate expiry check of connection {ConnectionId} failed", id);
            }
        }

        logger.LogInformation("SSO certificate expiry check: {Alerted} alert(s) of {Total} SAML connection(s)", alerted, ids.Count);
    }
}
