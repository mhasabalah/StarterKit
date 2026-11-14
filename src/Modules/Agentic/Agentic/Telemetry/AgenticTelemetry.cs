using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Agentic.Telemetry;

/// <summary>
/// Traces of the Agentic module (activity source <see cref="SourceName"/>, registered in
/// <c>Shared.Core/OpenTelemetryCollector</c>): one <c>agent.run</c> span per attempt, with an <c>agent.model_turn</c>
/// span per model request (the provider client's gen_ai span is its child) and an <c>agent.tool</c> span per tool
/// call. Spans carry ids, names, counts and outcomes only: never prompts, arguments or results.
/// </summary>
public static class AgenticTelemetry
{
    public const string SourceName = "StarterKit.Agentic";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// Starts the span of one attempt. It joins the trace of the request or message that started the run
    /// (<paramref name="traceParent"/>), so a run is found from the API call that created it.
    /// </summary>
    public static Activity? StartRun(AgentRun run, string? traceParent)
    {
        var activity = traceParent is not null && ActivityContext.TryParse(traceParent, null, out var parent)
            ? Source.StartActivity("agent.run", ActivityKind.Internal, parent)
            : Source.StartActivity("agent.run");
        activity?.SetTag("agent.run.id", run.Id);
        activity?.SetTag("agent.key", run.AgentKey);
        activity?.SetTag("agent.version", run.AgentVersion);
        activity?.SetTag("agent.trigger", run.TriggerKind.ToString());
        activity?.SetTag("agent.attempt", run.Attempts);
        activity?.SetTag("correlation.id", run.CorrelationId);
        return activity;
    }

    public static Activity? StartModelTurn(AgentRun run)
    {
        var activity = Source.StartActivity("agent.model_turn");
        activity?.SetTag("agent.run.id", run.Id);
        activity?.SetTag("agent.model.key", run.ModelKey);
        activity?.SetTag("agent.turn", run.ModelTurns + 1);
        return activity;
    }

    public static Activity? StartTool(AgentRun run, AgentRunStep step)
    {
        var activity = Source.StartActivity("agent.tool");
        activity?.SetTag("agent.run.id", run.Id);
        activity?.SetTag("agent.step.id", step.Id);
        activity?.SetTag("agent.tool.name", step.ToolName);
        return activity;
    }
}

/// <summary>
/// Metrics of the Agentic module (meter <c>StarterKit.Agentic</c>).
/// <para>
/// The backlog gauges (<c>agent.approvals.pending</c>, <c>agent.approvals.oldest_pending.age</c>,
/// <c>agent.runs.queued.oldest.age</c>) only read a snapshot CACHED here: the metrics callback never touches the database.
/// <c>RequeueStalledAgentRunsJob</c> refreshes it on every pass over all tenants. A snapshot older than
/// <see cref="BacklogStaleAfter"/> is not reported, so an instance that no longer runs the sweep drops its series instead
/// of exporting a frozen number (the outbox gauges work the same way).
/// </para>
/// </summary>
public sealed class AgenticMetrics : IDisposable
{
    public const string MeterName = "StarterKit.Agentic";
    public static readonly TimeSpan BacklogStaleAfter = TimeSpan.FromMinutes(20);

    private readonly Meter _meter;
    private readonly TimeProvider _time;
    private readonly Counter<long> _requeued;
    private BacklogSnapshot? _backlog;
    private readonly Counter<long> _started;
    private readonly Counter<long> _finished;
    private readonly Counter<long> _turns;
    private readonly Counter<long> _tokens;
    private readonly Counter<long> _toolCalls;
    private readonly Histogram<double> _duration;

    public AgenticMetrics(IMeterFactory meterFactory, TimeProvider time)
    {
        _time = time;
        _meter = meterFactory.Create(MeterName);
        _requeued = _meter.CreateCounter<long>("agent.runs.requeued", "{run}",
            "Runs the stall sweep queued again: no live lease and not queued recently (a worker died or a job was lost).");
        _meter.CreateObservableGauge("agent.approvals.pending", () => ReadBacklog(b => b.PendingApprovals), "{approval}",
            "Actions waiting for a person's decision, over all tenants.");
        _meter.CreateObservableGauge("agent.approvals.oldest_pending.age", () => ReadBacklog(b => b.OldestApprovalAgeSeconds), "s",
            "Age of the oldest action waiting for a decision (0 when none).");
        _meter.CreateObservableGauge("agent.runs.queued.oldest.age", () => ReadBacklog(b => b.OldestQueuedAgeSeconds), "s",
            "How long the oldest queued run has waited for a worker (0 when none).");
        _started = _meter.CreateCounter<long>("agent.runs.started", "{run}", "Agent runs started.");
        _finished = _meter.CreateCounter<long>("agent.runs.finished", "{run}", "Agent runs finished, by status and failure code.");
        _turns = _meter.CreateCounter<long>("agent.model.turns", "{turn}", "Model requests answered.");
        _tokens = _meter.CreateCounter<long>("agent.model.tokens", "{token}", "Model tokens, by direction.");
        _toolCalls = _meter.CreateCounter<long>("agent.tool.calls", "{call}", "Tool calls, by tool and outcome.");
        _duration = _meter.CreateHistogram<double>("agent.run.active.duration", "s", "Active time of finished agent runs.");
    }

    public void RecordStarted(AgentRun run) =>
        _started.Add(1, Tag("agent", run.AgentKey), Tag("trigger", run.TriggerKind.ToString()));

    public void RecordFinished(AgentRun run)
    {
        _finished.Add(1, Tag("agent", run.AgentKey), Tag("status", run.Status.ToString()),
            Tag("failure", run.FailureCode ?? "none"));
        _duration.Record(run.ActiveMilliseconds / 1000d, Tag("agent", run.AgentKey), Tag("status", run.Status.ToString()));
    }

    public void RecordModelTurn(AgentRun run, long inputTokens, long outputTokens)
    {
        _turns.Add(1, Tag("agent", run.AgentKey), Tag("model", run.ModelKey));
        _tokens.Add(inputTokens, Tag("agent", run.AgentKey), Tag("model", run.ModelKey), Tag("direction", "input"));
        _tokens.Add(outputTokens, Tag("agent", run.AgentKey), Tag("model", run.ModelKey), Tag("direction", "output"));
    }

    /// <param name="tool">A REGISTERED tool name, or <c>unknown</c> (never the raw text the model wrote: label cardinality).</param>
    public void RecordToolCall(string tool, string outcome) =>
        _toolCalls.Add(1, Tag("tool", tool), Tag("outcome", outcome));

    public void RecordRequeued(int runs)
    {
        if (runs > 0)
            _requeued.Add(runs);
    }

    public void RecordBacklog(long pendingApprovals, TimeSpan oldestApproval, TimeSpan oldestQueued) =>
        _backlog = new BacklogSnapshot(pendingApprovals, Math.Max(0, oldestApproval.TotalSeconds),
            Math.Max(0, oldestQueued.TotalSeconds), _time.GetUtcNow());

    private IEnumerable<Measurement<double>> ReadBacklog(Func<BacklogSnapshot, double> value)
    {
        if (_backlog is { } snapshot && _time.GetUtcNow() - snapshot.At <= BacklogStaleAfter)
            yield return new Measurement<double>(value(snapshot));
    }

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    private sealed record BacklogSnapshot(long PendingApprovals, double OldestApprovalAgeSeconds, double OldestQueuedAgeSeconds,
        DateTimeOffset At);

    public void Dispose() => _meter.Dispose();
}
