namespace Agentic.Domains.AgentRuns.Events;

/// <summary>A run reached a final state (completed, failed or cancelled). Dispatched after the finishing save committed.</summary>
public sealed record AgentRunFinishedEvent(
    Guid RunId,
    string AgentKey,
    AgentRunStatus Status,
    Guid InitiatedBy,
    string Culture,
    string? FailureCode) : IDomainEvent;
