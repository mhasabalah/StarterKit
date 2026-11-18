namespace Agentic.Contracts.Approvals;

/// <summary>Lifecycle of a request for a person to approve one action an agent proposed.</summary>
public enum AgentApprovalStatus
{
    /// <summary>Waiting for a decision. The run is suspended and holds no worker.</summary>
    [LocalizedDescription("Pending", "قيد الانتظار")]
    Pending = 1,

    /// <summary>Approved; the run is queued to execute exactly this action.</summary>
    [LocalizedDescription("Approved", "تمت الموافقة")]
    Approved = 2,

    [LocalizedDescription("Rejected", "مرفوض")]
    Rejected = 3,

    /// <summary>Nobody decided in time. The run failed with <c>approval_expired</c>.</summary>
    [LocalizedDescription("Expired", "منتهي الصلاحية")]
    Expired = 4,

    /// <summary>The run was cancelled before a decision.</summary>
    [LocalizedDescription("Cancelled", "ملغى")]
    Cancelled = 5,

    /// <summary>The approval was used: the action was started. An approval authorizes one execution of one payload.</summary>
    [LocalizedDescription("Used", "مستخدَمة")]
    Consumed = 6
}

/// <summary>
/// One proposed action waiting for (or after) a decision. <see cref="ArgumentsJson"/> is the exact payload that will
/// be executed and <see cref="PayloadHash"/> binds the decision to it: an approval covers this tool, this tool
/// version and these arguments, nothing else. <see cref="ModelRationale"/> is text the model wrote; it is shown for
/// context only and is not trusted.
/// </summary>
public sealed record AgentApprovalDto(
    Guid Id,
    Guid RunId,
    Guid StepId,
    string AgentKey,
    string ToolName,
    int ToolVersion,
    AgentToolRisk Risk,
    string? RequiredPermission,
    string ArgumentsJson,
    string PayloadHash,
    string? ModelRationale,
    AgentApprovalStatus Status,
    Guid RequestedBy,
    DateTime RequestedAt,
    DateTime ExpiresAt,
    Guid? DecidedBy,
    DateTime? DecidedAt,
    string? DecisionNote,
    long Version);

/// <summary>
/// Approves the proposed action. <paramref name="PayloadHash"/> must be the hash the approver was shown: a decision
/// is for one exact payload. The approver needs <c>agentic.approvals.decide</c> AND the tool's own permission.
/// </summary>
public sealed record ApproveAgentActionCommand(Guid ApprovalId, string PayloadHash, string? Note = null, long? Version = null)
    : ICommand<AgentApprovalDto>;

/// <summary>Rejects the proposed action. The run continues: the agent is told the action was rejected and why.</summary>
public sealed record RejectAgentActionCommand(Guid ApprovalId, string? Reason = null, long? Version = null)
    : ICommand<AgentApprovalDto>;

/// <summary>
/// Newest first: every approval of the tenant for holders of <c>agentic.approvals.decide</c>, otherwise the ones
/// requested by the caller's own runs.
/// </summary>
public sealed record GetAgentApprovalsQuery : BaseQuery, IQuery<PaginatedResult<AgentApprovalDto>>
{
    public AgentApprovalStatus? Status { get; init; }
    public Guid? RunId { get; init; }
}

public sealed record GetAgentApprovalByIdQuery(Guid ApprovalId) : IQuery<AgentApprovalDto>;
