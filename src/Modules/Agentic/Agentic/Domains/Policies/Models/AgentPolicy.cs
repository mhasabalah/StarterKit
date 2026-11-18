using Shared.Core.Auditing;

namespace Agentic.Domains.Policies.Models;

/// <summary>The values of a policy (see <see cref="AgentPolicy"/>).</summary>
public sealed record AgentPolicySettings(
    bool Enabled,
    AgentAutonomy? AutonomyCeiling,
    bool AllowAutomaticTriggers,
    bool RequireApprovalForChanges,
    int? MaxModelTurns,
    int? MaxToolCalls,
    long? MaxTokens)
{
    /// <summary>What applies when a tenant has set nothing: the agent as its definition and the module limits allow it.</summary>
    public static readonly AgentPolicySettings Default = new(true, null, true, false, null, null, null);
}

/// <summary>
/// One tenant's policy for one agent. It only ever tightens: whatever is stored here is combined with the agent's
/// definition and the module limits by taking the STRICTER of the two (see <c>AgentRunStarter</c>), so no value in this
/// row can give an agent more than its code allows. It is applied when a run starts and copied onto the run, so a
/// change never alters a run in progress.
/// Audited (<see cref="IAuditable"/>): who loosened or tightened an agent, and when.
/// </summary>
public class AgentPolicy : Aggregate<Guid>, IMustHaveTenant, IAuditable
{
    public Guid TenantId { get; set; }

    public string AgentKey { get; private set; } = default!;

    /// <summary>False: nobody in the tenant can start this agent.</summary>
    public bool Enabled { get; private set; } = true;

    public AgentAutonomy? AutonomyCeiling { get; private set; }

    /// <summary>False: events and schedules cannot start this agent in the tenant.</summary>
    public bool AllowAutomaticTriggers { get; private set; } = true;

    /// <summary>True: every change the agent proposes needs a person, whatever its risk.</summary>
    public bool RequireApprovalForChanges { get; private set; }

    public int? MaxModelTurns { get; private set; }
    public int? MaxToolCalls { get; private set; }
    public long? MaxTokens { get; private set; }

    private AgentPolicy()
    {
    }

    public static AgentPolicy Create(string agentKey, AgentPolicySettings settings)
    {
        var policy = new AgentPolicy { Id = Guid.CreateVersion7(), AgentKey = agentKey };
        policy.Update(settings);
        return policy;
    }

    public void Update(AgentPolicySettings settings)
    {
        if (settings.MaxModelTurns is < 1 || settings.MaxToolCalls is < 0 || settings.MaxTokens is < 1)
            throw new InvalidAgentRunStateException("budget_invalid");

        Enabled = settings.Enabled;
        AutonomyCeiling = settings.AutonomyCeiling;
        AllowAutomaticTriggers = settings.AllowAutomaticTriggers;
        RequireApprovalForChanges = settings.RequireApprovalForChanges;
        MaxModelTurns = settings.MaxModelTurns;
        MaxToolCalls = settings.MaxToolCalls;
        MaxTokens = settings.MaxTokens;
    }

    public AgentPolicySettings Settings => new(Enabled, AutonomyCeiling, AllowAutomaticTriggers, RequireApprovalForChanges,
        MaxModelTurns, MaxToolCalls, MaxTokens);
}
