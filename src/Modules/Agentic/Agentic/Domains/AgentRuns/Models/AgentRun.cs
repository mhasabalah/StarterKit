using Agentic.Domains.AgentRuns.Events;

namespace Agentic.Domains.AgentRuns.Models;

/// <summary>Per-run budgets, fixed when the run starts (module defaults lowered by the agent definition).</summary>
public readonly record struct AgentRunBudget(int MaxModelTurns, int MaxToolCalls, long MaxTokens, int MaxDurationSeconds);

/// <summary>
/// One execution of an agent for a user. The row is the source of truth: Hangfire only runs it, and the steps
/// (<see cref="AgentRunStep"/>) are both the transcript and the checkpoint a worker resumes from.
/// <list type="bullet">
/// <item><b>Lease.</b> A worker owns a running run through <see cref="LeaseOwner"/> / <see cref="LeaseUntil"/>, claimed
/// and renewed under optimistic concurrency, so two workers never process it at the same time. Every checkpoint saves
/// the run row with its steps, so the root version covers them.</item>
/// <item><b>Budgets.</b> Model turns, tool calls, tokens and active time are counted here and bounded by the budget
/// fixed at start: a run that loops stops by itself.</item>
/// <item><b>Identity.</b> <see cref="InitiatedBy"/> is the user the run acts for. It never carries permissions: they
/// are read again for every tool call. <see cref="PermissionCeiling"/> only ever narrows them (API-key scopes).</item>
/// <item><b>Cancellation.</b> A queued run is cancelled at once; a running one stops at its next checkpoint.</item>
/// </list>
/// Not <c>IAuditable</c>: the run and its steps ARE the record of what the agent did, and the counters change at every
/// checkpoint (pure noise in an audit trail).
/// </summary>
public class AgentRun : Aggregate<Guid>, IMustHaveTenant
{
    public const int AgentKeyMaxLength = 100;
    public const int ModelKeyMaxLength = 100;
    public const int TriggerKeyMaxLength = 200;
    public const int CultureMaxLength = 10;
    public const int LeaseOwnerMaxLength = 200;
    public const int FailureCodeMaxLength = 100;
    public const int FailureMessageMaxLength = 1000;
    public const int TraceParentMaxLength = 100;
    public const int MaxDepth = 3;

    public Guid TenantId { get; set; }

    public string AgentKey { get; private set; } = default!;
    public int AgentVersion { get; private set; }

    /// <summary>The configured model key the run was started with (<c>Agentic:Models:&lt;key&gt;</c>).</summary>
    public string ModelKey { get; private set; } = default!;

    /// <summary>The ceiling the run was started with (the definition's, lowered by a tenant policy).</summary>
    public AgentAutonomy Autonomy { get; private set; }

    public AgentRunStatus Status { get; private set; }
    public AgentTriggerKind TriggerKind { get; private set; }

    /// <summary>Dedupe key of an automatic trigger (unique per tenant and agent): the same event starts one run.</summary>
    public string? TriggerKey { get; private set; }

    /// <summary>The user the run acts for.</summary>
    public Guid InitiatedBy { get; private set; }

    /// <summary>
    /// When the run was started with an API key: the key's scopes. The run's effective permissions are the
    /// initiator's current permissions intersected with it, so a key never does more through an agent than directly.
    /// Null = no extra restriction.
    /// </summary>
    public string[]? PermissionCeiling { get; private set; }

    /// <summary>
    /// The API key that started the run, or null for an interactive user. A key reads and cancels only the runs it
    /// started itself (unless it holds the read scope): another key of the same owner may be scoped differently.
    /// </summary>
    public Guid? ApiKeyId { get; private set; }

    /// <summary>UI culture of the initiator (tool messages, failure messages, notifications).</summary>
    public string Culture { get; private set; } = "en";

    public string? Prompt { get; private set; }
    public string? InputJson { get; private set; }
    public string? OutputJson { get; private set; }
    public string? OutputText { get; private set; }

    public int MaxModelTurns { get; private set; }
    public int MaxToolCalls { get; private set; }
    public long MaxTokens { get; private set; }
    public int MaxDurationSeconds { get; private set; }

    public int ModelTurns { get; private set; }
    public int ToolCalls { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }
    public decimal EstimatedCost { get; private set; }

    /// <summary>Time spent in model requests and tools (waiting in the queue or for an approval does not count).</summary>
    public long ActiveMilliseconds { get; private set; }

    /// <summary>Sequence number of the next step.</summary>
    public int NextSequence { get; private set; } = 1;

    /// <summary>
    /// The run has read content the platform does not control (a tool marked <c>ReturnsUntrustedContent</c>). From
    /// then on every change needs a human approval, whatever the autonomy level.
    /// </summary>
    public bool Tainted { get; private set; }

    /// <summary>Times a worker claimed the run.</summary>
    public int Attempts { get; private set; }

    /// <summary>Failed attempts since the last saved checkpoint; the run fails when it reaches the limit.</summary>
    public int ConsecutiveFailures { get; private set; }

    public bool CancelRequested { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseUntil { get; private set; }

    /// <summary>Last time a Hangfire job was queued for this row (start, resume, retry, stall sweep).</summary>
    public DateTime? LastEnqueuedAt { get; private set; }

    /// <summary>Groups the runs of one conversation.</summary>
    public Guid? SessionId { get; private set; }

    /// <summary>The run that delegated to this one, and how deep the delegation is (bounded by <see cref="MaxDepth"/>).</summary>
    public Guid? ParentRunId { get; private set; }

    public int Depth { get; private set; }

    /// <summary>W3C trace parent of the request or message that started the run: its spans join that trace.</summary>
    public string? TraceParent { get; private set; }

    public string? FailureCode { get; private set; }
    public string? FailureMessage { get; private set; }

    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    public bool IsTerminal => Status is AgentRunStatus.Completed or AgentRunStatus.Failed or AgentRunStatus.Cancelled;

    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>Correlation id of everything the run does (logs, tool context, audit entries of its changes).</summary>
    public string CorrelationId => CorrelationIdOf(Id);

    public static string CorrelationIdOf(Guid runId) => $"agent-run:{runId:D}";

    private AgentRun()
    {
    }

    public static AgentRun Start(
        string agentKey,
        int agentVersion,
        string modelKey,
        AgentAutonomy autonomy,
        AgentTriggerKind triggerKind,
        Guid initiatedBy,
        string? prompt,
        string? inputJson,
        AgentRunBudget budget,
        string culture,
        IEnumerable<string>? permissionCeiling = null,
        string? triggerKey = null,
        Guid? sessionId = null,
        string? traceParent = null,
        Guid? apiKeyId = null)
    {
        if (string.IsNullOrWhiteSpace(agentKey) || agentKey.Length > AgentKeyMaxLength)
            throw new InvalidAgentRunStateException("agent_required");
        if (string.IsNullOrWhiteSpace(modelKey) || modelKey.Length > ModelKeyMaxLength)
            throw new InvalidAgentRunStateException("model_required");
        if (initiatedBy == Guid.Empty)
            throw new InvalidAgentRunStateException("initiator_required");
        if (string.IsNullOrWhiteSpace(prompt) && string.IsNullOrWhiteSpace(inputJson))
            throw new InvalidAgentRunStateException("input_required");
        if (budget.MaxModelTurns < 1 || budget.MaxToolCalls < 0 || budget.MaxTokens < 1 || budget.MaxDurationSeconds < 1)
            throw new InvalidAgentRunStateException("budget_invalid");
        if (triggerKey is { Length: > TriggerKeyMaxLength })
            throw new InvalidAgentRunStateException("trigger_key_too_long");

        return new AgentRun
        {
            Id = Guid.CreateVersion7(),
            AgentKey = agentKey,
            AgentVersion = agentVersion,
            ModelKey = modelKey,
            Autonomy = autonomy,
            Status = AgentRunStatus.Queued,
            TriggerKind = triggerKind,
            TriggerKey = string.IsNullOrWhiteSpace(triggerKey) ? null : triggerKey,
            InitiatedBy = initiatedBy,
            PermissionCeiling = permissionCeiling?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Culture = AgenticText.NormalizeCulture(culture),
            Prompt = string.IsNullOrWhiteSpace(prompt) ? null : prompt,
            InputJson = string.IsNullOrWhiteSpace(inputJson) ? null : inputJson,
            MaxModelTurns = budget.MaxModelTurns,
            MaxToolCalls = budget.MaxToolCalls,
            MaxTokens = budget.MaxTokens,
            MaxDurationSeconds = budget.MaxDurationSeconds,
            SessionId = sessionId,
            ApiKeyId = apiKeyId,
            TraceParent = TextLimits.ClipOrNull(traceParent, TraceParentMaxLength)
        };
    }

    // ----- lease ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Takes the run for <paramref name="owner"/>: false when it is finished, suspended, or another worker holds a live
    /// lease. Saved with the optimistic version, so of two concurrent claims only one commits.
    /// </summary>
    public bool TryClaim(string owner, DateTime now, TimeSpan lease)
    {
        if (IsTerminal || Status == AgentRunStatus.AwaitingApproval)
            return false;
        if (Status == AgentRunStatus.Running && LeaseOwner != owner && LeaseUntil > now)
            return false;

        Status = AgentRunStatus.Running;
        StartedAt ??= now;
        LeaseOwner = TextLimits.Clip(owner, LeaseOwnerMaxLength);
        LeaseUntil = now + lease;
        Attempts++;
        return true;
    }

    public bool HoldsLease(string owner) => Status == AgentRunStatus.Running && LeaseOwner == owner;

    public void RenewLease(DateTime now, TimeSpan lease) => LeaseUntil = now + lease;

    public void MarkEnqueued(DateTime now) => LastEnqueuedAt = now;

    /// <summary>
    /// Needs to be queued again: waiting or running without a live lease, and not queued within
    /// <paramref name="quietPeriod"/> (a lost enqueue, a crashed worker, a lease that expired).
    /// </summary>
    public bool IsStalled(DateTime now, TimeSpan quietPeriod) =>
        Status is AgentRunStatus.Queued or AgentRunStatus.Running &&
        (LeaseUntil is null || LeaseUntil < now) &&
        (LastEnqueuedAt is null || LastEnqueuedAt + quietPeriod < now);

    /// <summary>
    /// A failed attempt gives the run back to the queue. Returns the number of consecutive failures (the caller fails
    /// the run instead once the limit is reached).
    /// </summary>
    public int ReleaseForRetry(DateTime now)
    {
        EnsureNotTerminal();
        Status = AgentRunStatus.Queued;
        ReleaseLease();
        ConsecutiveFailures++;
        LastEnqueuedAt = now;
        return ConsecutiveFailures;
    }

    /// <summary>Server shutdown in the middle of an attempt: back to the queue, not counted as a failure.</summary>
    public void ReleaseForShutdown()
    {
        if (IsTerminal)
            return;
        Status = AgentRunStatus.Queued;
        ReleaseLease();
    }

    private void ReleaseLease()
    {
        LeaseOwner = null;
        LeaseUntil = null;
    }

    // ----- budgets ----------------------------------------------------------------------------------------------

    /// <summary>The budget that forbids another model turn (<c>max_model_turns</c>, <c>max_tokens</c>, <c>max_duration</c>), or null.</summary>
    public string? ExhaustedBudget()
    {
        if (ModelTurns >= MaxModelTurns)
            return "max_model_turns";
        if (TotalTokens >= MaxTokens)
            return "max_tokens";
        return ActiveMilliseconds >= MaxDurationSeconds * 1000L ? "max_duration" : null;
    }

    public bool ToolCallBudgetExhausted => ToolCalls >= MaxToolCalls;

    // ----- steps ------------------------------------------------------------------------------------------------

    /// <summary>Records one answered model request (the step is saved by the caller in the same unit of work).</summary>
    public AgentRunStep RecordModelTurn(string? text, string? modelId, string? finishReason, long inputTokens,
        long outputTokens, decimal cost, long durationMs, DateTime now)
    {
        EnsureRunning();
        ModelTurns++;
        InputTokens += Math.Max(0, inputTokens);
        OutputTokens += Math.Max(0, outputTokens);
        EstimatedCost += Math.Max(0, cost);
        ActiveMilliseconds += Math.Max(0, durationMs);
        ConsecutiveFailures = 0;
        return AgentRunStep.ModelTurn(Id, NextSequence++, text, modelId, finishReason, inputTokens, outputTokens,
            durationMs, now);
    }

    /// <summary>A tool call the model asked for in <paramref name="turn"/>; handled later, one call at a time.</summary>
    public AgentRunStep PlanToolCall(AgentRunStep turn, string callId, string toolName, string argumentsJson)
    {
        EnsureRunning();
        return AgentRunStep.ToolCall(Id, NextSequence++, turn.Sequence, callId, toolName, argumentsJson);
    }

    /// <summary>A tool call reached a final state (executed, refused or skipped): counters and taint.</summary>
    public void RecordToolCall(AgentRunStep step, bool executed, bool untrustedContent)
    {
        EnsureRunning();
        if (executed)
            ToolCalls++;
        ActiveMilliseconds += Math.Max(0, step.DurationMs);
        Tainted |= untrustedContent;
        ConsecutiveFailures = 0;
    }

    // ----- cancellation -----------------------------------------------------------------------------------------

    /// <summary>A waiting run is cancelled at once; a running one at its next checkpoint.</summary>
    public void RequestCancel(DateTime now)
    {
        EnsureNotTerminal();
        if (Status is AgentRunStatus.Queued or AgentRunStatus.AwaitingApproval)
        {
            Finish(AgentRunStatus.Cancelled, now);
            return;
        }

        CancelRequested = true;
    }

    /// <summary>The worker saw <see cref="CancelRequested"/> at a checkpoint.</summary>
    public void ConfirmCancelled(DateTime now) => Finish(AgentRunStatus.Cancelled, now);

    // ----- completion -------------------------------------------------------------------------------------------

    /// <summary>Finished with a typed result (<paramref name="outputJson"/>) or with the model's answer.</summary>
    public void Complete(string? outputJson, string? outputText, DateTime now)
    {
        EnsureRunning();
        OutputJson = outputJson;
        OutputText = outputText;
        Finish(AgentRunStatus.Completed, now);
    }

    public void Fail(string code, string message, DateTime now)
    {
        FailureCode = TextLimits.Clip(code, FailureCodeMaxLength);
        FailureMessage = TextLimits.Clip(message, FailureMessageMaxLength);
        Finish(AgentRunStatus.Failed, now);
    }

    private void Finish(AgentRunStatus status, DateTime now)
    {
        EnsureNotTerminal();
        Status = status;
        FinishedAt = now;
        CancelRequested = false;
        ReleaseLease();
        AddDomainEvent(new AgentRunFinishedEvent(Id, AgentKey, status, InitiatedBy, Culture, FailureCode));
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal)
            throw new InvalidAgentRunStateException("already_finished");
    }

    private void EnsureRunning()
    {
        if (Status != AgentRunStatus.Running)
            throw new InvalidAgentRunStateException("not_running");
    }
}

/// <summary>Column-length guard of the run entities (values longer than the column are cut, never rejected).</summary>
internal static class TextLimits
{
    public static string Clip(string value, int max) => value.Length <= max ? value : value[..max];

    public static string? ClipOrNull(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : Clip(value, max);
}
