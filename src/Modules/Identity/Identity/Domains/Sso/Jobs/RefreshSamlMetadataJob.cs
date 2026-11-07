using Hangfire;
using Identity.Data.Repository.Sso;
using Identity.Options;
using Identity.Services.Sso;
using Microsoft.Extensions.Options;

namespace Identity.Domains.Sso.Jobs;

/// <summary>
/// Recurring job: re-reads the IdP metadata URL of every active SAML connection that has one, so an IdP's certificate
/// rollover never locks a company out. SSO connections are control-plane rows (explicit <c>TenantId</c>), so they are
/// queried directly; every connection is its own unit of work, so one broken IdP never blocks the others and the
/// change events are published after each commit.
/// </summary>
[Queue("default")]
public class RefreshSamlMetadataJob(
    IUnitOfWorkRunner unitOfWork,
    IOptions<SsoOptions> options,
    ILogger<RefreshSamlMetadataJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            return;

        var ids = await unitOfWork.RunAsync(
            (services, token) => services.GetRequiredService<ISsoConnectionRepository>().GetRefreshableSamlIdsAsync(token), ct);

        int updated = 0, unchanged = 0, failed = 0;
        foreach (var id in ids)
        {
            try
            {
                var result = await unitOfWork.RunAsync(async (services, token) =>
                {
                    var connections = services.GetRequiredService<ISsoConnectionRepository>();
                    var connection = await connections.GetByIdAsync(id, token);
                    return connection is null
                        ? null
                        : await services.GetRequiredService<SamlMetadataRefresher>().RefreshAsync(connection, token);
                }, ct);

                switch (result?.Outcome)
                {
                    case Models.SamlMetadataOutcome.Updated:
                        updated++;
                        break;
                    case Models.SamlMetadataOutcome.Unchanged:
                        unchanged++;
                        break;
                    case Models.SamlMetadataOutcome.Failed:
                        failed++;
                        break;
                }
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict())
            {
                // An admin edited the connection at the same moment: the next run picks it up.
                logger.LogInformation("SAML metadata refresh of connection {ConnectionId} lost a concurrency race", id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                logger.LogError(ex, "SAML metadata refresh of connection {ConnectionId} failed", id);
            }
        }

        logger.LogInformation(
            "SAML metadata refresh: {Updated} updated, {Unchanged} unchanged, {Failed} failed of {Total}",
            updated, unchanged, failed, ids.Count);
    }
}
