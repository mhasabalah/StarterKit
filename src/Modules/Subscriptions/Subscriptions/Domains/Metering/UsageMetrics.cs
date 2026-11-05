using System.Diagnostics.Metrics;
using Shared.Core.OpenTelemetryCollector.DiagnosticsProvider;

namespace Subscriptions.Domains.Metering;

/// <summary>Counters for the metering path: store failures (where the quota fails open) and denied consumptions.</summary>
public sealed class UsageMetrics
{
    public const string StoreFailuresInstrument = "metering.store.failures";
    public const string QuotaDeniedInstrument = "metering.quota.denied";

    private readonly Counter<long> _storeFailures;
    private readonly Counter<long> _quotaDenied;

    public UsageMetrics(IDiagnosticsProvider? diagnostics = null)
    {
        var meter = diagnostics?.Meter ?? new Meter("StarterKit.Metering");
        _storeFailures = meter.CreateCounter<long>(
            StoreFailuresInstrument, description: "Usage counter store failures (quota checks fail open)");
        _quotaDenied = meter.CreateCounter<long>(
            QuotaDeniedInstrument, description: "Requests refused because a plan quota was used up");
    }

    public void StoreFailure(string operation) =>
        _storeFailures.Add(1, new KeyValuePair<string, object?>("operation", operation));

    public void QuotaDenied(string feature) =>
        _quotaDenied.Add(1, new KeyValuePair<string, object?>("feature", feature));
}
