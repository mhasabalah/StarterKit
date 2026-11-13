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

/// <summary>Metrics of the Agentic module (meter <c>StarterKit.Agentic</c>).</summary>
public sealed class AgenticMetrics : IDisposable
{
    public const string MeterName = "StarterKit.Agentic";

    private readonly Meter _meter;
    private readonly Counter<long> _started;
    private readonly Counter<long> _finished;
    private readonly Counter<long> _turns;
    private readonly Counter<long> _tokens;
    private readonly Counter<long> _toolCalls;
    private readonly Histogram<double> _duration;

    public AgenticMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
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

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    public void Dispose() => _meter.Dispose();
}
