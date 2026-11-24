using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Search.Engine.Meilisearch;

/// <summary>
/// <c>GET /health</c> of the Meilisearch server. Registered with <see cref="HealthStatus.Degraded"/> as the failure
/// status: a search engine outage must not take the API out of rotation, because owners fall back to their SQL search.
/// </summary>
internal sealed class MeilisearchHealthCheck(MeilisearchClient client) : IHealthCheck
{
    public const string Name = "search-engine";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await client.IsHealthyAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Meilisearch is available")
            : new HealthCheckResult(context.Registration.FailureStatus, "Meilisearch did not answer /health");
}
