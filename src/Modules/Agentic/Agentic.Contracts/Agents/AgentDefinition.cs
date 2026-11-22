using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Contracts.Agents;

/// <summary>
/// How much an agent may do on its own. A ceiling enforced by the tool gateway for every call; nothing a model or a
/// caller sends can raise it above what the definition declares.
/// </summary>
public enum AgentAutonomy
{
    /// <summary>Reads and summarizes.</summary>
    [LocalizedDescription("Observe", "مراقبة")]
    Observe = 1,

    /// <summary>Reads and suggests actions. Changes nothing.</summary>
    [LocalizedDescription("Recommend", "توصية")]
    Recommend = 2,

    /// <summary>Also creates drafts and reversible changes (<see cref="AgentToolRisk.LowRiskMutation"/>).</summary>
    [LocalizedDescription("Prepare", "تحضير")]
    Prepare = 3,

    /// <summary>Also executes sensitive and external actions, each after the human approval its risk demands.</summary>
    [LocalizedDescription("Execute", "تنفيذ")]
    Execute = 4
}

/// <summary>What started a run.</summary>
public enum AgentTriggerKind
{
    /// <summary>A signed-in user, interactively.</summary>
    [LocalizedDescription("User", "مستخدم")]
    User = 1,

    /// <summary>An API key.</summary>
    [LocalizedDescription("API", "واجهة برمجية")]
    Api = 2,

    /// <summary>A domain or integration event.</summary>
    [LocalizedDescription("Event", "حدث")]
    Event = 3,

    /// <summary>A recurring job.</summary>
    [LocalizedDescription("Schedule", "جدولة")]
    Schedule = 4,

    /// <summary>Started by another agent's <c>agent.delegate</c> call, for the same person (ADR-0033).</summary>
    [LocalizedDescription("Another agent", "وكيل آخر")]
    Delegated = 5,

    /// <summary>One tool call by an external MCP client, recorded as a run (ADR-0034); never started otherwise.</summary>
    [LocalizedDescription("MCP client", "عميل MCP")]
    Mcp = 6
}

/// <summary>Per-run budgets an agent may LOWER below the module defaults (<c>Agentic:Limits</c>); never raise.</summary>
public sealed record AgentLimits(
    int? MaxModelTurns = null,
    int? MaxToolCalls = null,
    long? MaxTokens = null,
    int? MaxDurationSeconds = null);

/// <summary>
/// An agent: instructions, the tools it may request, and its ceiling. Defined in code by the module that owns the use
/// case and registered with <see cref="AgentServiceCollectionExtensions.AddAgent"/>, so it is reviewed and versioned
/// like any other code. Business RULES do not belong in <see cref="Instructions"/>: they stay in the commands the
/// tools call.
/// </summary>
public sealed record AgentDefinition
{
    /// <summary>Stable key <c>&lt;module&gt;.&lt;agent-name&gt;</c>, e.g. <c>shipping.shipment-preparation</c>.</summary>
    public required string Key { get; init; }

    /// <summary>Raise it when instructions, tools or the output type change.</summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// The oldest version whose suspended runs this definition can still continue. A run started before it stops with
    /// <c>agent_version_retired</c> instead of resuming with instructions it was not started under.
    /// </summary>
    public int MinResumableVersion { get; init; } = 1;

    public required LocalizedText Name { get; init; }

    public LocalizedText? Description { get; init; }

    /// <summary>The system prompt (role, goal, working method). The platform adds its own safety preamble.</summary>
    public required string Instructions { get; init; }

    /// <summary>Allowlist: names of the registered tools this agent may request. Anything else is refused.</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    public AgentAutonomy Autonomy { get; init; } = AgentAutonomy.Recommend;

    /// <summary>Key of a configured model (<c>Agentic:Models:&lt;key&gt;</c>); the module default when null.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// Typed result. When set, the run finishes only when the model hands in a value of this type (validated, and by
    /// an <c>IValidator</c> of the type when one is registered). When null, the run finishes with the model's text.
    /// </summary>
    public Type? OutputType { get; init; }

    /// <summary>Tenant permission needed to start this agent, on top of <c>agentic.runs.start</c>.</summary>
    public string? RequiredPermission { get; init; }

    /// <summary>Explicit trigger policy: a run requested by any other kind of trigger is refused.</summary>
    public IReadOnlyList<AgentTriggerKind> AllowedTriggers { get; init; } = [AgentTriggerKind.User, AgentTriggerKind.Api];

    public AgentLimits? Limits { get; init; }

    /// <summary>
    /// True: people can hold a conversation with this agent (<c>/api/v1/agent-conversations</c>). Every message is a
    /// run that sees the earlier turns of its conversation (each turn's message and final answer). Task agents that do
    /// one job per run leave it false.
    /// </summary>
    public bool Conversational { get; init; }

    /// <summary>
    /// Agents this one may hand a task to with the built-in tool <c>agent.delegate</c> (ADR-0033). Each must allow the
    /// trigger <see cref="AgentTriggerKind.Delegated"/>. The delegated run acts for the same person, at the lower of the
    /// two autonomies, starts tainted when this run is, and its tokens count against this run's budget.
    /// </summary>
    public IReadOnlyList<string> Delegates { get; init; } = [];

    /// <summary>
    /// Offers this agent's tools to external MCP clients at <c>/api/v1/mcp/{key}</c> (ADR-0034), when
    /// <c>Agentic:Mcp:Enabled</c>. A client acts as the person (or API key) that signs in, only with the tools that person
    /// may run right now without an approval: in practice, read-only tools.
    /// </summary>
    public bool McpServer { get; init; }
}

public static class AgentServiceCollectionExtensions
{
    /// <summary>Registers an agent. Keys must be unique; every listed tool must be registered (checked at startup).</summary>
    public static IServiceCollection AddAgent(this IServiceCollection services, AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        services.AddSingleton(definition);
        return services;
    }
}

// ----- read side ------------------------------------------------------------------------------------------------

public sealed record AgentDto(
    string Key,
    int Version,
    LocalizedText Name,
    LocalizedText? Description,
    AgentAutonomy Autonomy,
    bool HasTypedOutput,
    bool Conversational = false);

public sealed record AgentToolDto(
    string Name,
    int Version,
    string Description,
    AgentToolRisk Risk,
    string? Permission);

public sealed record AgentDetailDto(AgentDto Agent, IReadOnlyList<AgentToolDto> Tools);

/// <summary>The agents the caller may start.</summary>
public sealed record GetAgentsQuery : IQuery<IReadOnlyList<AgentDto>>;

public sealed record GetAgentByKeyQuery(string Key) : IQuery<AgentDetailDto>;
