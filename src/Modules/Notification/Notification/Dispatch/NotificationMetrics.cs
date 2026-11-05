using System.Diagnostics.Metrics;

namespace Notification.Dispatch;

/// <summary>
/// Counters of the additive channels (meter <see cref="MeterName"/>, exported by OpenTelemetry). Open hub connections
/// come from ASP.NET Core's own <c>signalr.server.active_connections</c> (meter <c>Microsoft.AspNetCore.Http.Connections</c>).
/// </summary>
public sealed class NotificationMetrics
{
    public const string MeterName = "StarterKit.Notification";

    private readonly Counter<long> _inApp;
    private readonly Counter<long> _push;
    private readonly Counter<long> _realtime;

    public NotificationMetrics(IMeterFactory? meterFactory = null)
    {
        var meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName);
        _inApp = meter.CreateCounter<long>("notification.inapp.delivered", description: "Inbox entries created");
        _push = meter.CreateCounter<long>("notification.push.sent", description: "Push sends per device, by result");
        _realtime = meter.CreateCounter<long>("notification.realtime.sent", description: "Real-time hub events, by method and result");
    }

    public void InAppDelivered() => _inApp.Add(1);

    public void PushSent(string result, long count = 1)
    {
        if (count > 0)
            _push.Add(count, new KeyValuePair<string, object?>("result", result));
    }

    public void RealtimeSent(string method, bool success) =>
        _realtime.Add(1,
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("result", success ? "ok" : "error"));
}
