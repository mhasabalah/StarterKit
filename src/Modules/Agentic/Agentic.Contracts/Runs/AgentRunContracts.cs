namespace Agentic.Contracts.Runs;

/// <summary>Lifecycle of an agent run. The row is the source of truth; real-time events only mirror it.</summary>
public enum AgentRunStatus
{
    /// <summary>Waiting for a worker (just started, resumed after an approval, or waiting for a retry).</summary>
    [LocalizedDescription("Queued", "في قائمة الانتظار")]
    Queued = 1,

    [LocalizedDescription("Running", "قيد التنفيذ")]
    Running = 2,

    /// <summary>Suspended until a human approves or rejects a proposed action. No worker is held.</summary>
    [LocalizedDescription("Awaiting approval", "بانتظار الموافقة")]
    AwaitingApproval = 3,

    [LocalizedDescription("Completed", "مكتمل")]
    Completed = 4,

    [LocalizedDescription("Failed", "فشل")]
    Failed = 5,

    [LocalizedDescription("Cancelled", "ملغى")]
    Cancelled = 6
}

public enum AgentRunStepKind
{
    /// <summary>One request to the model: its text and the tool calls it asked for.</summary>
    [LocalizedDescription("Model turn", "دور النموذج")]
    ModelTurn = 1,

    /// <summary>One tool call the model asked for, with what the gateway did about it.</summary>
    [LocalizedDescription("Tool call", "استدعاء أداة")]
    ToolCall = 2
}

public enum AgentRunStepStatus
{
    /// <summary>Requested by the model, not handled yet.</summary>
    [LocalizedDescription("Pending", "قيد الانتظار")]
    Pending = 1,

    /// <summary>The tool is running, or was running when a worker stopped.</summary>
    [LocalizedDescription("Executing", "قيد التنفيذ")]
    Executing = 2,

    [LocalizedDescription("Succeeded", "نجح")]
    Succeeded = 3,

    /// <summary>Refused by the gateway or failed in the tool. The model was told why.</summary>
    [LocalizedDescription("Failed", "فشل")]
    Failed = 4,

    /// <summary>A human rejected the proposed action.</summary>
    [LocalizedDescription("Rejected", "مرفوض")]
    Rejected = 5,

    /// <summary>Waiting for a human decision.</summary>
    [LocalizedDescription("Awaiting approval", "بانتظار الموافقة")]
    AwaitingApproval = 6,

    /// <summary>Not executed because the run finished first.</summary>
    [LocalizedDescription("Skipped", "تم تخطيه")]
    Skipped = 7
}

// ----- DTOs -----------------------------------------------------------------------------------------------------

/// <summary>An agent run as listed. <see cref="Version"/> is the optimistic-concurrency token.</summary>
public sealed record AgentRunDto(
    Guid Id,
    string AgentKey,
    int AgentVersion,
    AgentRunStatus Status,
    AgentTriggerKind TriggerKind,
    Guid InitiatedBy,
    int ModelTurns,
    int ToolCalls,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCost,
    bool CancelRequested,
    string? FailureCode,
    string? FailureMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    long Version);

/// <summary>
/// A run with what was asked and what came out. <see cref="OutputJson"/> is the typed result of an agent with an
/// output type; <see cref="OutputText"/> the answer of a free-text agent.
/// </summary>
public sealed record AgentRunDetailDto(AgentRunDto Run, string? Prompt, string? InputJson, string? OutputJson, string? OutputText);

/// <summary>One step of the transcript.</summary>
public sealed record AgentRunStepDto(
    Guid Id,
    int Sequence,
    AgentRunStepKind Kind,
    AgentRunStepStatus Status,
    string? ToolName,
    int? ToolVersion,
    string? Text,
    string? ArgumentsJson,
    string? ResultJson,
    string? ErrorCode,
    long InputTokens,
    long OutputTokens,
    long DurationMs,
    DateTime? StartedAt,
    DateTime? FinishedAt);

// ----- commands -------------------------------------------------------------------------------------------------

/// <summary>
/// Starts a run of <paramref name="AgentKey"/> for the current user and queues it (Hangfire queue <c>agentic</c>).
/// Returns at once: the run executes in the background.
/// </summary>
/// <param name="AgentKey">A registered agent.</param>
/// <param name="Prompt">The goal in natural language.</param>
/// <param name="InputJson">Optional structured input (a JSON object) given to the agent next to the prompt.</param>
/// <param name="SessionId">Optional: groups runs of one conversation.</param>
public sealed record StartAgentRunCommand(
    string AgentKey,
    string? Prompt = null,
    string? InputJson = null,
    Guid? SessionId = null) : ICommand<AgentRunDto>;

/// <summary>
/// Cancels a run. A waiting run is cancelled at once; a running one stops at its next checkpoint (what its tools
/// already committed stays committed). Only the initiator or a holder of <c>agentic.manage</c>.
/// </summary>
/// <param name="Version">Optional optimistic-concurrency token (<see cref="AgentRunDto.Version"/>).</param>
public sealed record CancelAgentRunCommand(Guid RunId, long? Version = null) : ICommand<AgentRunDto>;

// ----- queries --------------------------------------------------------------------------------------------------

/// <summary>Newest first: the caller's own runs, or every run of the tenant with <c>agentic.runs.read</c>.</summary>
public sealed record GetAgentRunsQuery : BaseQuery, IQuery<PaginatedResult<AgentRunDto>>
{
    public string? AgentKey { get; init; }
    public AgentRunStatus? Status { get; init; }
}

public sealed record GetAgentRunByIdQuery(Guid RunId) : IQuery<AgentRunDetailDto>;

/// <summary>The transcript of a run, in order.</summary>
public sealed record GetAgentRunStepsQuery(Guid RunId, int? PageNumber = 1, int? PageSize = 50)
    : IQuery<PaginatedResult<AgentRunStepDto>>;
