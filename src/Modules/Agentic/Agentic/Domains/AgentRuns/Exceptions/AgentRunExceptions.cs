namespace Agentic.Domains.AgentRuns.Exceptions;

// Every message comes from AgenticResource (en/ar) through AgenticText, in the current UI culture: the request culture
// in a handler, the initiator's culture inside a job. Error codes stay stable for clients.

public sealed class AgentNotFoundException(string key)
    : NotFoundException(AgenticText.Get("Agent.Error.agent_not_found", key));

/// <summary>Also thrown for a run of another user the caller may not read (no id probing).</summary>
public sealed class AgentRunNotFoundException(Guid id)
    : NotFoundException(AgenticText.Get("Agent.Error.run_not_found", id));

/// <summary>The module is switched off (<c>Agentic:Enabled=false</c>): its endpoints do not exist.</summary>
public sealed class AgenticDisabledException()
    : NotFoundException(AgenticText.Get("Agent.Error.disabled"));

/// <summary>An operation the run's current state does not allow (400 <c>invalid_run_state</c>).</summary>
public sealed class InvalidAgentRunStateException : BadRequestException
{
    /// <param name="reason">Resx key suffix: <c>Agent.State.&lt;reason&gt;</c>.</param>
    public InvalidAgentRunStateException(string reason)
        : base(AgenticText.Get("Agent.State." + reason)) => ErrorCode = "invalid_run_state";
}

/// <summary>The caller lacks a permission the agent needs, or the agent may not be started this way (403).</summary>
public sealed class AgentAccessDeniedException : ForbiddenException
{
    public AgentAccessDeniedException(string code, params object?[] args)
        : base(AgenticText.Get("Agent.Error." + code, args)) => ErrorCode = code;
}

/// <summary>Another worker took the run over (its lease expired): this attempt stops without touching the row.</summary>
public sealed class AgentRunLeaseLostException(Guid runId)
    : InvalidOperationException($"Agent run {runId} is now owned by another worker.");
