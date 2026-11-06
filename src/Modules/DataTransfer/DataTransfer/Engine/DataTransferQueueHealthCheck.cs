using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DataTransfer.Engine;

/// <summary>
/// Reports whether ANY live Hangfire server (this instance or a worker replica) processes the <c>data-transfer</c>
/// queue. Turning the queue off everywhere by mistake (<c>Hangfire:Servers:DataTransfer:Enabled=false</c> on the API
/// without a running worker) would otherwise leave every import/export pending forever while the API still answers 202.
/// Registered with <see cref="HealthStatus.Degraded"/>: the rest of the API works, so it must not leave rotation.
/// </summary>
public sealed class DataTransferQueueHealthCheck(JobStorage storage, TimeProvider timeProvider) : IHealthCheck
{
    public const string Name = "datatransfer-queue";

    /// <summary>A server whose heartbeat is older than this is considered gone (Hangfire's own timeout is 5 minutes).</summary>
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(5);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var servers = storage.GetMonitoringApi().Servers()
            .Where(s => s.Queues.Contains(DataTransferOptions.Queue, StringComparer.Ordinal))
            .Count(s => s.Heartbeat is { } beat && now - beat < HeartbeatTimeout);

        return Task.FromResult(servers > 0
            ? HealthCheckResult.Healthy($"{servers} server(s) process the '{DataTransferOptions.Queue}' queue")
            : new HealthCheckResult(context.Registration.FailureStatus,
                $"No live Hangfire server processes the '{DataTransferOptions.Queue}' queue; imports and exports stay pending. " +
                "Enable Hangfire:Servers:DataTransfer on this instance or run a worker (docker-compose.worker.yml)."));
    }
}
