using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Attachment.Infrastructure.Validation.ClamAv;

/// <summary>
/// <c>zPING</c> → <c>PONG</c>. Registered with <see cref="HealthStatus.Degraded"/> as the failure status: a scanner
/// outage must not take the whole API out of rotation; uploads fail closed on their own (503).
/// </summary>
public sealed class ClamAvHealthCheck(ClamAvMalwareScanner scanner) : IHealthCheck
{
    public const string Name = "clamav";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await scanner.PingAsync(cancellationToken)
            ? HealthCheckResult.Healthy("clamd answered PONG")
            : new HealthCheckResult(context.Registration.FailureStatus, "clamd did not answer PING");
}
