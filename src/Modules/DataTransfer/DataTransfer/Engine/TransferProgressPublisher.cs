using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Notification.Contracts.Realtime;

namespace DataTransfer.Engine;

/// <summary>
/// Live progress for the requester's open sessions: a <see cref="UserEvent"/> of type <see cref="EventType"/> over the
/// notification hub. The runner calls it after each saved checkpoint (throttled to one event per job per
/// <see cref="DataTransferOptions.ProgressIntervalMilliseconds"/>), and the finished-event handler sends the final
/// state. Best effort: the job row (<c>GET /data-transfer/jobs/{id}</c>) stays the source of truth, and a client that
/// reconnects re-reads it.
/// <para>
/// Split deployments: a worker replica's send reaches sockets on the API replicas only through the SignalR Redis
/// backplane (<c>Realtime:UseRedisBackplane</c>), the same requirement as for any multi-replica hub.
/// </para>
/// </summary>
public sealed class TransferProgressPublisher(
    IUserEventPublisher events,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IOptions<DataTransferOptions> options)
{
    public const string EventType = "datatransfer.job-progress";

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastSent = new();

    /// <summary>Sends the job's state unless one was sent for it within the interval (a final state is always sent).</summary>
    public Task PublishAsync(TransferJob job, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        if (job.IsTerminal)
        {
            _lastSent.TryRemove(job.Id, out _);
        }
        else
        {
            var interval = TimeSpan.FromMilliseconds(options.Value.ProgressIntervalMilliseconds);
            if (_lastSent.TryGetValue(job.Id, out var last) && now - last < interval)
                return Task.CompletedTask;
            _lastSent[job.Id] = now;
        }

        return events.PublishAsync(job.RequestedBy, new UserEvent(EventType, tenantContext.TenantId, Payload(job)),
            cancellationToken);
    }

    /// <summary>Ids, counters and states only (the same numbers as the REST detail). Enums as their numeric value.</summary>
    public static IReadOnlyDictionary<string, object?> Payload(TransferJob job) => new Dictionary<string, object?>
    {
        ["jobId"] = job.Id,
        ["kind"] = (int)job.Kind,
        ["definitionKey"] = job.DefinitionKey,
        ["status"] = (int)job.Status,
        ["totalRows"] = job.TotalRows,
        ["processedRows"] = job.ProcessedRows,
        ["createdRows"] = job.CreatedRows,
        ["updatedRows"] = job.UpdatedRows,
        ["unchangedRows"] = job.UnchangedRows,
        ["failedRows"] = job.FailedRows,
        ["cancelRequested"] = job.CancelRequested,
        ["failureCode"] = job.FailureCode
    };
}
