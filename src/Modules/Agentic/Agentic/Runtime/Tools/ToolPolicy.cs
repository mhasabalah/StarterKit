namespace Agentic.Runtime.Tools;

public enum ToolDecisionKind
{
    /// <summary>The call may run now.</summary>
    Execute,

    /// <summary>The call is not allowed. It is not executed; the model is told why.</summary>
    Refuse,

    /// <summary>The call is allowed only after a human approved exactly this action.</summary>
    RequireApproval
}

/// <param name="Code">Stable reason of a refusal (<c>tool_not_allowed</c>, <c>forbidden</c>, <c>autonomy_exceeded</c>).</param>
public readonly record struct ToolDecision(ToolDecisionKind Kind, string? Code = null)
{
    public static readonly ToolDecision Execute = new(ToolDecisionKind.Execute);
    public static readonly ToolDecision RequireApproval = new(ToolDecisionKind.RequireApproval);

    public static ToolDecision Refuse(string code) => new(ToolDecisionKind.Refuse, code);
}

/// <summary>
/// The authorization decision for one tool call, as a pure function of trusted facts: the registered agent and tool,
/// the run, and the initiator's CURRENT permissions. Nothing the model wrote takes part in it except the tool's name,
/// which only selects what is being decided. An instruction such as "ignore previous rules" in a prompt, a document or
/// a tool result cannot change the outcome, because no text is consulted here.
/// </summary>
public static class ToolPolicy
{
    public const string ToolNotAllowed = "tool_not_allowed";
    public const string Forbidden = "forbidden";
    public const string AutonomyExceeded = "autonomy_exceeded";

    /// <param name="agent">The agent of the run.</param>
    /// <param name="tool">The registered tool the model named, or null when there is none.</param>
    /// <param name="autonomy">The run's ceiling.</param>
    /// <param name="tainted">The run has read untrusted content.</param>
    /// <param name="permissions">The initiator's effective tenant permissions right now.</param>
    public static ToolDecision Decide(RegisteredAgent agent, AgentToolDescriptor? tool, AgentAutonomy autonomy,
        bool tainted, IReadOnlySet<string> permissions)
    {
        // 1. Allowlist. An unknown tool and a known tool outside the agent's list get the same answer.
        if (tool is null || !agent.Allows(tool.Name))
            return ToolDecision.Refuse(ToolNotAllowed);

        // 2. The initiator's own permission for this operation (never the agent's, the job server's or the model's).
        if (!tool.AllowAnyMember && (tool.Permission is null || !permissions.Contains(tool.Permission)))
            return ToolDecision.Refuse(Forbidden);

        // 3. Autonomy ceiling against the tool's risk.
        if (tool.RequiredAutonomy > autonomy)
            return ToolDecision.Refuse(AutonomyExceeded);

        // 4. Human approval: always for sensitive and external actions and for tools that demand it, and for ANY
        //    change once the run has read content the platform does not control (instructions hidden in a document
        //    must not move data).
        if (tool.Risk is AgentToolRisk.SensitiveMutation or AgentToolRisk.ExternalOrIrreversible ||
            tool.RequiresApproval || (tainted && tool.IsMutation))
        {
            return ToolDecision.RequireApproval;
        }

        return ToolDecision.Execute;
    }
}
