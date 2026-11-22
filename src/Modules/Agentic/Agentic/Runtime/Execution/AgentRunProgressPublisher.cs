using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Notification.Contracts.Realtime;

namespace Agentic.Runtime.Execution;

/// <summary>
/// Live progress for the initiator's open sessions: a <see cref="UserEvent"/> of type <see cref="EventType"/> over the
/// notification hub, sent after saved checkpoints. Ordinary progress is throttled to one event per run per
/// <see cref="AgenticOptions.ProgressIntervalMilliseconds"/>; a change of status (started, waiting for approval,
/// resumed, completed, failed, cancelled) is always sent. Best effort and never the source of truth: the run row
/// (<c>GET /agent-runs/{id}</c>) is, and a client that reconnects reads it again.
/// <para>
/// The payload holds ids, names, counters and states only: no prompt, no tool arguments and no results.
/// </para>
/// <para>
/// Split deployments: a worker replica's send reaches sockets on the API replicas only through the SignalR Redis
/// backplane (<c>Realtime:UseRedisBackplane</c>), the same requirement as for any multi-replica hub.
/// </para>
/// </summary>
public sealed class AgentRunProgressPublisher(
    IUserEventPublisher events,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    ILogger<AgentRunProgressPublisher> logger)
{
    public const string EventType = "agentic.run-progress";

    private readonly ConcurrentDictionary<Guid, (DateTimeOffset At, AgentRunStatus Status)> _lastSent = new();

    /// <param name="run">The run as just saved.</param>
    /// <param name="step">The step the checkpoint was about, when there is one.</param>
    public async Task PublishAsync(AgentRun run, AgentRunStep? step = null)
    {
        try
        {
            var now = timeProvider.GetUtcNow();
            if (run.IsTerminal)
            {
                _lastSent.TryRemove(run.Id, out _);
            }
            else
            {
                var interval = TimeSpan.FromMilliseconds(options.Value.ProgressIntervalMilliseconds);
                if (_lastSent.TryGetValue(run.Id, out var last) && last.Status == run.Status && now - last.At < interval)
                    return;
                _lastSent[run.Id] = (now, run.Status);
            }

            await events.PublishAsync(run.InitiatedBy, new UserEvent(EventType, tenantContext.TenantId, Payload(run, step)),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Progress must never fail a checkpoint that was already saved.
            logger.LogDebug(ex, "Agent run {RunId}: progress event not sent", run.Id);
        }
    }

    /// <summary>Enums as their numeric value, like the REST API.</summary>
    public static IReadOnlyDictionary<string, object?> Payload(AgentRun run, AgentRunStep? step) => new Dictionary<string, object?>
    {
        ["runId"] = run.Id,
        ["agentKey"] = run.AgentKey,
        ["conversationId"] = run.ConversationId,
        ["status"] = (int)run.Status,
        ["modelTurns"] = run.ModelTurns,
        ["toolCalls"] = run.ToolCalls,
        ["cancelRequested"] = run.CancelRequested,
        ["failureCode"] = run.FailureCode,
        ["stepSequence"] = step?.Sequence,
        ["stepKind"] = step is null ? null : (int)step.Kind,
        ["stepStatus"] = step is null ? null : (int)step.Status,
        ["tool"] = step?.Kind == AgentRunStepKind.ToolCall ? step.ToolName : null
    };
}
