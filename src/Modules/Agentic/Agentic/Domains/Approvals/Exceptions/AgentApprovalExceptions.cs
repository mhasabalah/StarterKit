namespace Agentic.Domains.Approvals.Exceptions;

/// <summary>Also thrown for an approval the caller may not see (no id probing).</summary>
public sealed class AgentApprovalNotFoundException(Guid id)
    : NotFoundException(AgenticText.Get("Agent.Error.approval_not_found", id));

/// <summary>An operation the approval's current state does not allow (400 <c>invalid_approval_state</c>).</summary>
public sealed class InvalidAgentApprovalStateException : BadRequestException
{
    /// <param name="reason">Resx key suffix: <c>Agent.Approval.&lt;reason&gt;</c>.</param>
    public InvalidAgentApprovalStateException(string reason)
        : base(AgenticText.Get("Agent.Approval." + reason)) => ErrorCode = "invalid_approval_state";
}

/// <summary>
/// The decision cannot be recorded as asked (409): the request is no longer pending, it expired, or the payload the
/// approver was shown is not the payload on record, or the run stopped waiting. The code tells which
/// (<c>approval_not_pending</c>, <c>approval_expired</c>, <c>approval_payload_mismatch</c>, <c>run_not_waiting</c>).
/// </summary>
public sealed class AgentApprovalConflictException : ConflictException
{
    public AgentApprovalConflictException(string code)
        : base(AgenticText.Get("Agent.Approval." + code)) => ErrorCode = code;
}

/// <summary>The approver may not decide this action (403): see the code.</summary>
public sealed class AgentApprovalDeniedException : ForbiddenException
{
    public AgentApprovalDeniedException(string code, params object?[] args)
        : base(AgenticText.Get("Agent.Approval." + code, args)) => ErrorCode = code;
}
