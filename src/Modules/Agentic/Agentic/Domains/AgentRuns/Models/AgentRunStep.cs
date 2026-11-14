namespace Agentic.Domains.AgentRuns.Models;

/// <summary>
/// One entry of a run's transcript: a model turn, or a tool call the model asked for. Steps are appended in
/// <see cref="Sequence"/> order and are the checkpoint a worker resumes from: the conversation sent to the model is
/// rebuilt from them, never kept in memory across attempts.
/// <para>
/// Prompts, arguments and results are stored here because a resumed run needs them. They are never written to logs or
/// traces by default, and tools must not return secrets.
/// </para>
/// </summary>
public class AgentRunStep : Entity<Guid>, IMustHaveTenant
{
    public const int ToolNameMaxLength = 150;
    public const int CallIdMaxLength = 200;
    public const int ErrorCodeMaxLength = 100;
    public const int ModelIdMaxLength = 200;
    public const int FinishReasonMaxLength = 50;

    public Guid TenantId { get; set; }

    public Guid RunId { get; private set; }

    /// <summary>Position in the run (1-based, unique per run).</summary>
    public int Sequence { get; private set; }

    public AgentRunStepKind Kind { get; private set; }
    public AgentRunStepStatus Status { get; private set; }

    // ----- model turn -----

    /// <summary>The model's text of this turn (may be empty when it only asked for tools).</summary>
    public string? Text { get; private set; }

    /// <summary>The provider's model id that answered.</summary>
    public string? ModelId { get; private set; }

    public string? FinishReason { get; private set; }
    public long InputTokens { get; private set; }
    public long OutputTokens { get; private set; }

    // ----- tool call -----

    /// <summary>Sequence of the model turn that asked for this call.</summary>
    public int? TurnSequence { get; private set; }

    /// <summary>The provider's id of the call; the result is reported back under it.</summary>
    public string? CallId { get; private set; }

    /// <summary>The tool name as the model wrote it (it may name a tool that does not exist).</summary>
    public string? ToolName { get; private set; }

    /// <summary>Version of the registered tool the call was evaluated against.</summary>
    public int? ToolVersion { get; private set; }

    /// <summary>The arguments exactly as the model produced them (untrusted; not necessarily valid JSON).</summary>
    public string? ArgumentsJson { get; private set; }

    /// <summary>What the model was told: the tool's JSON output, or <c>{"error":{"code","message"}}</c>.</summary>
    public string? ResultJson { get; private set; }

    /// <summary>Set when the call was refused or failed (<c>forbidden</c>, <c>invalid_arguments</c>, ...).</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>Times the tool was started (above 1: a worker stopped while it ran and it was run again).</summary>
    public int Executions { get; private set; }

    public long DurationMs { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    /// <summary>Not handled to the end yet: the run cannot ask the model again before it is.</summary>
    public bool IsOpen => Status is AgentRunStepStatus.Pending or AgentRunStepStatus.Executing
        or AgentRunStepStatus.AwaitingApproval;

    private AgentRunStep()
    {
    }

    internal static AgentRunStep ModelTurn(Guid runId, int sequence, string? text, string? modelId, string? finishReason,
        long inputTokens, long outputTokens, long durationMs, DateTime now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            RunId = runId,
            Sequence = sequence,
            Kind = AgentRunStepKind.ModelTurn,
            Status = AgentRunStepStatus.Succeeded,
            Text = string.IsNullOrEmpty(text) ? null : text,
            ModelId = TextLimits.ClipOrNull(modelId, ModelIdMaxLength),
            FinishReason = TextLimits.ClipOrNull(finishReason, FinishReasonMaxLength),
            InputTokens = Math.Max(0, inputTokens),
            OutputTokens = Math.Max(0, outputTokens),
            DurationMs = Math.Max(0, durationMs),
            StartedAt = now.AddMilliseconds(-Math.Max(0, durationMs)),
            FinishedAt = now
        };

    internal static AgentRunStep ToolCall(Guid runId, int sequence, int turnSequence, string callId, string toolName,
        string argumentsJson) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            RunId = runId,
            Sequence = sequence,
            Kind = AgentRunStepKind.ToolCall,
            Status = AgentRunStepStatus.Pending,
            TurnSequence = turnSequence,
            CallId = TextLimits.Clip(callId, CallIdMaxLength),
            ToolName = TextLimits.Clip(toolName, ToolNameMaxLength),
            ArgumentsJson = argumentsJson
        };

    /// <summary>Checkpoint BEFORE the tool runs: a step found in this state later was interrupted.</summary>
    public void MarkExecuting(int toolVersion, DateTime now)
    {
        EnsureOpen();
        Status = AgentRunStepStatus.Executing;
        ToolVersion = toolVersion;
        Executions++;
        StartedAt ??= now;
    }

    /// <summary>The call is allowed only after a person approved it: the run is suspended on this step.</summary>
    public void AwaitApproval(int toolVersion)
    {
        EnsureOpen();
        Status = AgentRunStepStatus.AwaitingApproval;
        ToolVersion = toolVersion;
    }

    /// <summary>A person rejected the action; <paramref name="resultJson"/> is what the model is told.</summary>
    public void Reject(string resultJson, DateTime now) =>
        Close(AgentRunStepStatus.Rejected, "rejected", resultJson, 0, now);

    public void Succeed(string resultJson, long durationMs, DateTime now) =>
        Close(AgentRunStepStatus.Succeeded, null, resultJson, durationMs, now);

    /// <summary>Refused by the gateway or failed in the tool; <paramref name="resultJson"/> is what the model is told.</summary>
    public void FailWith(string errorCode, string resultJson, long durationMs, DateTime now, int? toolVersion = null)
    {
        ToolVersion ??= toolVersion;
        Close(AgentRunStepStatus.Failed, errorCode, resultJson, durationMs, now);
    }

    /// <summary>Not executed: the run finished (a result was handed in) before this call was reached.</summary>
    public void Skip(string resultJson, DateTime now) =>
        Close(AgentRunStepStatus.Skipped, "skipped", resultJson, 0, now);

    private void Close(AgentRunStepStatus status, string? errorCode, string resultJson, long durationMs, DateTime now)
    {
        EnsureOpen();
        Status = status;
        ErrorCode = TextLimits.ClipOrNull(errorCode, ErrorCodeMaxLength);
        ResultJson = resultJson;
        DurationMs = Math.Max(0, durationMs);
        StartedAt ??= now;
        FinishedAt = now;
    }

    private void EnsureOpen()
    {
        if (Kind != AgentRunStepKind.ToolCall || !IsOpen)
            throw new InvalidAgentRunStateException("step_closed");
    }
}
