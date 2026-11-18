using MassTransit;
using Shared.Contracts.Messaging;

namespace Agentic.Contracts.Runs.IntegrationEvents;

/// <summary>
/// Integration event: an agent run completed. The module that owns the agent can react (store the result with its own
/// aggregate, move a workflow on). Ids only; read the result with <see cref="GetAgentRunByIdQuery"/> and re-read your
/// own state: delivery is at-least-once and unordered. The tenant travels as a header. The urn never changes.
/// </summary>
[MessageUrn("agentic.agent-run-completed.v1")]
public sealed class AgentRunCompletedIntegrationEvent : IntegrationEvent
{
    public Guid RunId { get; init; }
    public string AgentKey { get; init; } = default!;
    public Guid InitiatedBy { get; init; }

    /// <summary>The trigger key the run was requested with (automatic triggers), to match it to what asked for it.</summary>
    public string? TriggerKey { get; init; }

    /// <summary>The run produced a typed result (an agent with an output type).</summary>
    public bool HasTypedOutput { get; init; }
}

/// <summary>
/// Integration event: an agent run failed (a budget, the model provider, lost access of the initiator, an expired
/// approval, ...). Cancelled runs raise nothing. <see cref="FailureCode"/> is the stable code of the run.
/// </summary>
[MessageUrn("agentic.agent-run-failed.v1")]
public sealed class AgentRunFailedIntegrationEvent : IntegrationEvent
{
    public Guid RunId { get; init; }
    public string AgentKey { get; init; } = default!;
    public Guid InitiatedBy { get; init; }
    public string? TriggerKey { get; init; }
    public string FailureCode { get; init; } = default!;
}
