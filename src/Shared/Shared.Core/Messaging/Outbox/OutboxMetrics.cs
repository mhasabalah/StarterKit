using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Shared.Core.Messaging.Outbox;

/// <summary>
/// Outbox backlog gauges, tagged <c>context</c> (the DbContext type name):
/// <c>outbox.messages.pending</c> (not delivered, not dead-lettered) and <c>outbox.messages.dead_lettered</c>.
/// <para>
/// The gauges only read values CACHED here - the metrics callback never touches the database. <see cref="OutboxSweepJob"/>
/// refreshes them every run (default every minute). A snapshot older than <see cref="StaleAfter"/> is not reported, so an
/// instance that no longer runs the sweep (Hangfire picks one server per run) drops its series instead of exporting a
/// frozen number. Tenant-data contexts are summed over every serviceable tenant, dedicated databases included.
/// </para>
/// </summary>
public sealed class OutboxMetrics
{
    public const string PendingInstrument = "outbox.messages.pending";
    public const string DeadLetteredInstrument = "outbox.messages.dead_lettered";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Snapshot> _snapshots = new();

    public OutboxMetrics(Meter meter, TimeProvider time)
    {
        _time = time;
        meter.CreateObservableGauge(PendingInstrument, () => Read(s => s.Pending), "{message}",
            "Outbox rows waiting for delivery (including retries in back-off)");
        meter.CreateObservableGauge(DeadLetteredInstrument, () => Read(s => s.DeadLettered), "{message}",
            "Outbox rows given up on; they need an operator (requeue or fix)");
    }

    public void Record(string context, long pending, long deadLettered) =>
        _snapshots[context] = new Snapshot(pending, deadLettered, _time.GetUtcNow());

    private IEnumerable<Measurement<long>> Read(Func<Snapshot, long> value)
    {
        var now = _time.GetUtcNow();
        foreach (var (context, snapshot) in _snapshots)
        {
            if (now - snapshot.At > StaleAfter)
                continue;
            yield return new Measurement<long>(value(snapshot), new KeyValuePair<string, object?>("context", context));
        }
    }

    private sealed record Snapshot(long Pending, long DeadLettered, DateTimeOffset At);
}
