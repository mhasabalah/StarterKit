namespace Notification.Channels.Push;

/// <summary>A rendered push message (one language, already redacted when sensitive).</summary>
/// <param name="CollapseKey">
/// The notification's correlation id. Used as Android <c>tag</c>, APNs <c>apns-collapse-id</c> and Web Push <c>tag</c>:
/// a duplicate delivery (provider-side redelivery, a job re-run) REPLACES the shown notification instead of adding one.
/// </param>
public sealed record PushMessage(
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data,
    string CollapseKey,
    TimeSpan TimeToLive);

public sealed record PushTarget(Guid DeviceId, string Token, PushPlatform Platform);

public enum PushSendOutcome
{
    /// <summary>Accepted by the provider for delivery.</summary>
    Delivered = 0,

    /// <summary>The token is dead (app uninstalled, token rotated, other sender): delete the device.</summary>
    InvalidToken = 1,

    /// <summary>Provider unavailable / throttled: retry this device later.</summary>
    Transient = 2,

    /// <summary>Rejected for a reason a retry cannot fix (invalid payload, APNs auth misconfigured): keep the device, do not retry.</summary>
    Rejected = 3,
}

public sealed record PushSendResult(Guid DeviceId, PushSendOutcome Outcome, string? Error = null);

/// <summary>Sends one message to many devices. Returns one result per target; never throws for per-device failures.</summary>
public interface IPushProvider
{
    string Name { get; }

    Task<IReadOnlyList<PushSendResult>> SendAsync(PushMessage message, IReadOnlyList<PushTarget> targets, CancellationToken ct);
}

/// <summary>Logs instead of sending (development, tests, projects without Firebase).</summary>
public sealed class NoOpPushProvider(ILogger<NoOpPushProvider> logger) : IPushProvider
{
    public string Name => "NoOp";

    public Task<IReadOnlyList<PushSendResult>> SendAsync(PushMessage message, IReadOnlyList<PushTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(targets);
        logger.LogInformation(
            "[NoOp push] {Count} device(s) {Devices} Title: {Title} CollapseKey: {CollapseKey}",
            targets.Count, string.Join(", ", targets.Select(t => $"{t.Platform}:{PushDevice.Hint(t.Token)}")),
            message.Title, message.CollapseKey);

        IReadOnlyList<PushSendResult> results = targets.Select(t => new PushSendResult(t.DeviceId, PushSendOutcome.Delivered)).ToList();
        return Task.FromResult(results);
    }
}
