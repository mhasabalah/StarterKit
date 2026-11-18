namespace Agentic.Contracts.Policies;

/// <summary>
/// What a tenant allows one agent to do. A policy can only TIGHTEN what the agent's definition and the module limits
/// allow: switch the agent off, lower its autonomy, lower its budgets, demand a person for every change. It can never
/// widen anything. <see cref="IsDefault"/> is true when the tenant has set nothing (the values shown are the defaults).
/// </summary>
public sealed record AgentPolicyDto(
    string AgentKey,
    bool Enabled,
    AgentAutonomy? AutonomyCeiling,
    bool AllowAutomaticTriggers,
    bool RequireApprovalForChanges,
    int? MaxModelTurns,
    int? MaxToolCalls,
    long? MaxTokens,
    bool IsDefault,
    long Version);

public sealed record GetAgentPolicyQuery(string AgentKey) : IQuery<AgentPolicyDto>;

/// <summary>
/// Sets the tenant's policy for an agent (created on first use). Applies to runs started afterwards; a run in progress
/// keeps the ceiling and budgets it was started with.
/// </summary>
/// <param name="Enabled">False: nobody in the tenant can start this agent.</param>
/// <param name="AutonomyCeiling">Lowers the agent's autonomy for this tenant; null keeps the definition's.</param>
/// <param name="AllowAutomaticTriggers">False: events and schedules cannot start this agent in the tenant.</param>
/// <param name="RequireApprovalForChanges">True: every change the agent proposes needs a person, whatever its risk.</param>
/// <param name="Version">Optimistic-concurrency token of the existing policy.</param>
public sealed record UpdateAgentPolicyCommand(
    string AgentKey,
    bool Enabled = true,
    AgentAutonomy? AutonomyCeiling = null,
    bool AllowAutomaticTriggers = true,
    bool RequireApprovalForChanges = false,
    int? MaxModelTurns = null,
    int? MaxToolCalls = null,
    long? MaxTokens = null,
    long? Version = null) : ICommand<AgentPolicyDto>;
