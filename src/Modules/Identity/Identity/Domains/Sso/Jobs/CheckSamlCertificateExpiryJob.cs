using Hangfire;
using Identity.Data.Repository.Sso;
using Identity.Domains.Sso.Events;
using Identity.Options;
using Microsoft.Extensions.Options;

namespace Identity.Domains.Sso.Jobs;

/// <summary>
/// Daily job: finds SAML connections whose IdP signing certificates are about to expire and tells the tenant's SSO
/// administrators (<see cref="SsoCertificateExpiringEvent"/>) once per expiry date and threshold.
/// <list type="bullet">
/// <item>The alert is CLAIMED first with a conditional set-based update, so two replicas (or a re-run) never send it
/// twice and a concurrent admin edit never conflicts (no version bump).</item>
/// <item>The event is then published; the handler lets delivery failures propagate, and the job RELEASES the claim so
/// the next run retries. Recipients that were already notified before a failure may be notified again (at-least-once).</item>
/// <item>One unit of work per connection: a failing connection never stops the others.</item>
/// </list>
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
        if (!options.Value.Enabled || !options.Value.CertificateExpiryAlertsEnabled || thresholds.Length == 0)
            return;

        var ids = await unitOfWork.RunAsync(
            (services, token) => services.GetRequiredService<ISsoConnectionRepository>().GetActiveSamlIdsAsync(token), ct);

        var alerted = 0;
        foreach (var id in ids)
        {
            try
            {
                alerted += await unitOfWork.RunAsync((services, token) => CheckAsync(services, id, thresholds, token), ct);
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

    private async Task<int> CheckAsync(IServiceProvider services, Guid id, int[] thresholds, CancellationToken ct)
    {
        var connections = services.GetRequiredService<ISsoConnectionRepository>();
        var connection = await connections.GetByIdAsync(id, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (connection?.ExpiryAlertDue(now, thresholds) is not { } alert)
            return 0;

        var previousNotAfter = connection.SamlExpiryAlertedNotAfter;
        var previousDays = connection.SamlExpiryAlertedDays;
        if (!await connections.TryClaimExpiryAlertAsync(id, alert.NotAfter, alert.ThresholdDays, ct))
            return 0; // another replica (or run) already sent this alert

        try
        {
            await services.GetRequiredService<IPublisher>().Publish(
                new SsoCertificateExpiringEvent(connection.Id, connection.TenantId, connection.Name, alert.NotAfter, alert.DaysLeft), ct);
        }
        catch
        {
            await connections.ReleaseExpiryAlertAsync(
                id, alert.NotAfter, alert.ThresholdDays, previousNotAfter, previousDays, CancellationToken.None);
            throw;
        }

        return 1;
    }
}
