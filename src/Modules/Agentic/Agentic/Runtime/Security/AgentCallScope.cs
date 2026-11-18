namespace Agentic.Runtime.Security;

/// <summary>
/// The tool call being executed in this DI scope, set by the executor before it invokes a tool. Agentic's own tools
/// (<c>knowledge.search</c>) read the run's EFFECTIVE permissions from here: the initiator's current permissions
/// narrowed by the run's API-key ceiling, exactly what <c>ToolPolicy</c> decided with. Business modules never need it:
/// their commands see the initiator through <c>IHttpContextService</c> and the tenant scope as usual.
/// </summary>
public sealed class AgentCallScope
{
    public AgentPrincipal? Principal { get; private set; }

    public Guid? RunId { get; private set; }

    public void Enter(AgentPrincipal principal, Guid runId)
    {
        if (Principal is not null)
            throw new InvalidOperationException("A DI scope executes one tool call.");
        Principal = principal;
        RunId = runId;
    }
}
