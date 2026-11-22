using System.Text;
using Agentic.Runtime.Models;
using Microsoft.Extensions.Options;
using Notification.Contracts.Realtime;

namespace Agentic.Runtime.Execution;

/// <summary>
/// Opens the text stream of one model turn: the answer's text as the model writes it, sent to the initiator's open
/// sessions as <see cref="UserEvent"/>s of type <see cref="EventType"/> (ADR-0030).
/// <para>
/// Only runs started by a person or an API key stream (nobody watches an event or schedule run), and only while
/// <see cref="AgentStreamingOptions.Enabled"/>. Unlike progress events, these carry content, by design: the text of the
/// initiator's own answer, sent only to the initiator's own sockets.
/// </para>
/// </summary>
public sealed class AgentRunTextStreams(
    IUserEventPublisher events,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    ILogger<AgentRunTextStreams> logger)
{
    public const string EventType = "agentic.run-text";

    /// <summary>A stream for the next model turn of <paramref name="run"/>, or null when this run does not stream.</summary>
    public AgentRunTextStream? Open(AgentRun run)
    {
        var settings = options.Value;
        if (!settings.Streaming.Enabled || run.TriggerKind is not (AgentTriggerKind.User or AgentTriggerKind.Api))
            return null;

        return new AgentRunTextStream(events, tenantContext.TenantId, run, run.ModelTurns + 1, timeProvider,
            TimeSpan.FromMilliseconds(settings.Streaming.IntervalMilliseconds), settings.Limits.MaxModelTextBytes, logger);
    }
}

/// <summary>
/// The text of one model turn, collected and sent at most once per interval, then once more when the turn ends
/// (<see cref="DisposeAsync"/>). Each event carries the turn number and the offset of its first character, so a client
/// appends in order and starts over when a retried turn begins again at offset 0. A preview only: the saved run is the
/// answer. Best effort: a failed send is logged and never fails the turn.
/// </summary>
public sealed class AgentRunTextStream : IModelTextSink, IAsyncDisposable
{
    private readonly IUserEventPublisher _events;
    private readonly Guid? _tenantId;
    private readonly AgentRun _run;
    private readonly int _turn;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;
    private readonly int _maxBytes;
    private readonly ILogger _logger;
    private readonly StringBuilder _pending = new();
    private long _lastSent;
    private int _offset;
    private int _bytes;
    private bool _capped;

    internal AgentRunTextStream(IUserEventPublisher events, Guid? tenantId, AgentRun run, int turn,
        TimeProvider timeProvider, TimeSpan interval, int maxBytes, ILogger logger)
    {
        _events = events;
        _tenantId = tenantId;
        _run = run;
        _turn = turn;
        _timeProvider = timeProvider;
        _interval = interval;
        _maxBytes = maxBytes;
        _logger = logger;
        _lastSent = timeProvider.GetTimestamp();
    }

    public async ValueTask WriteAsync(string delta, CancellationToken cancellationToken)
    {
        if (_capped || delta.Length == 0)
            return;

        // An answer above the size limit stops the run (the executor never stores it): stop previewing it too.
        _bytes += Encoding.UTF8.GetByteCount(delta);
        if (_bytes > _maxBytes)
        {
            _capped = true;
            _pending.Clear();
            return;
        }

        _pending.Append(delta);
        if (_timeProvider.GetElapsedTime(_lastSent) >= _interval)
            await FlushAsync();
    }

    public async ValueTask DisposeAsync() => await FlushAsync();

    private async Task FlushAsync()
    {
        if (_pending.Length == 0)
            return;

        var text = _pending.ToString();
        _pending.Clear();
        var offset = _offset;
        _offset += text.Length;
        _lastSent = _timeProvider.GetTimestamp();
        try
        {
            await _events.PublishAsync(_run.InitiatedBy, new UserEvent(AgentRunTextStreams.EventType, _tenantId, Payload(_run, _turn, offset, text)),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogDebug(ex, "Agent run {RunId}: text event not sent", _run.Id);
        }
    }

    public static IReadOnlyDictionary<string, object?> Payload(AgentRun run, int turn, int offset, string delta) =>
        new Dictionary<string, object?>
        {
            ["runId"] = run.Id,
            ["conversationId"] = run.ConversationId,
            ["turn"] = turn,
            ["offset"] = offset,
            ["delta"] = delta
        };
}
