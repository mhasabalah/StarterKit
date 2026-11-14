using System.Diagnostics.Metrics;
using Agentic.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Tests;

/// <summary>
/// The backlog gauges report the sweep's last snapshot, and nothing once it is stale: a stopped sweep must not export a
/// frozen backlog that keeps an alert quiet (or firing).
/// </summary>
public class AgenticMetricsTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static Dictionary<string, double> Observe(AgenticMetrics metrics)
    {
        _ = metrics;
        var values = new Dictionary<string, double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AgenticMetrics.MeterName)
                    l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => values[instrument.Name] = value);
        listener.Start();
        listener.RecordObservableInstruments();
        return values;
    }

    [Fact]
    public void TheBacklog_IsReported_UntilItIsStale_ThenDropped()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var clock = new Clock(new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero));
        using var metrics = new AgenticMetrics(services.GetRequiredService<IMeterFactory>(), clock);

        Observe(metrics).Should().BeEmpty("nothing is reported before the first sweep");

        metrics.RecordBacklog(3, TimeSpan.FromHours(5), TimeSpan.FromMinutes(12));
        var reported = Observe(metrics);
        reported["agent.approvals.pending"].Should().Be(3);
        reported["agent.approvals.oldest_pending.age"].Should().Be(5 * 3600);
        reported["agent.runs.queued.oldest.age"].Should().Be(12 * 60);

        clock.Now += AgenticMetrics.BacklogStaleAfter + TimeSpan.FromSeconds(1);
        Observe(metrics).Should().BeEmpty("a sweep that stopped must not leave a frozen number behind");
    }
}
