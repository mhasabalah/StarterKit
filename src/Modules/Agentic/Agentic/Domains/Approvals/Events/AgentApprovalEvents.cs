using Agentic.Contracts.Approvals;

namespace Agentic.Domains.Approvals.Events;

/// <summary>An action an agent proposed needs a person's decision. The run is suspended.</summary>
public sealed record AgentApprovalRequestedEvent(
    Guid ApprovalId,
    Guid RunId,
    string AgentKey,
    string ToolName,
    Guid RequestedBy,
    string Culture) : IDomainEvent;

/// <summary>A person approved or rejected a proposed action. The run is queued again.</summary>
public sealed record AgentApprovalDecidedEvent(
    Guid ApprovalId,
    Guid RunId,
    AgentApprovalStatus Status,
    Guid DecidedBy) : IDomainEvent;
