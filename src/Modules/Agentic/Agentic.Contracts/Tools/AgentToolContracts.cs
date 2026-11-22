using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Contracts.Tools;

/// <summary>
/// How dangerous a tool is. Together with the run's <see cref="AgentAutonomy"/> it decides, in code, whether the tool
/// may run at all and whether a human must approve the call first. The model has no say in either.
/// </summary>
public enum AgentToolRisk
{
    /// <summary>Reads only. Allowed at every autonomy level.</summary>
    [LocalizedDescription("Read only", "قراءة فقط")]
    ReadOnly = 1,

    /// <summary>Creates a draft or makes a change that is easy to undo. Needs <see cref="AgentAutonomy.Prepare"/>.</summary>
    [LocalizedDescription("Low-risk change", "تغيير منخفض المخاطر")]
    LowRiskMutation = 2,

    /// <summary>Changes business state. Needs <see cref="AgentAutonomy.Execute"/> and a human approval.</summary>
    [LocalizedDescription("Sensitive change", "تغيير حساس")]
    SensitiveMutation = 3,

    /// <summary>
    /// Leaves the system (an external call, money, a message to a customer) or cannot be undone. Needs
    /// <see cref="AgentAutonomy.Execute"/> and ALWAYS a human approval; no policy can waive it.
    /// </summary>
    [LocalizedDescription("External or irreversible action", "إجراء خارجي أو غير قابل للتراجع")]
    ExternalOrIrreversible = 4
}

/// <summary>
/// Declares an agent tool. Put it on the class that implements <see cref="IAgentTool{TInput,TOutput}"/> and register
/// the class with <see cref="AgentToolServiceCollectionExtensions.AddAgentTool{TTool}"/>.
/// </summary>
/// <param name="name">
/// Stable name <c>&lt;module&gt;.&lt;verb_noun&gt;</c> in lower snake case, e.g. <c>shipping.get_shipment</c>. Never
/// rename a tool: runs in progress and pending approvals refer to it.
/// </param>
/// <param name="description">What the tool does and when to use it, for the model (English, one or two sentences).</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AgentToolAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    /// <summary>
    /// Raise it when the input or the effect changes. A pending approval is bound to the version it was requested
    /// for, so a changed tool is proposed again instead of being executed with stale consent.
    /// </summary>
    public int Version { get; init; } = 1;

    public AgentToolRisk Risk { get; init; } = AgentToolRisk.ReadOnly;

    /// <summary>
    /// Tenant permission (a <c>TenantPermissions</c> value) the INITIATING USER must hold when the call runs. It is
    /// re-read for every call, so access removed while a run is suspended is honoured.
    /// </summary>
    public string? Permission { get; init; }

    /// <summary>
    /// Explicit opt-out of <see cref="Permission"/>: any active member of the tenant may use the tool. A tool must
    /// declare one of the two (checked when the registry is built), like endpoints must declare their authorization.
    /// </summary>
    public bool AllowAnyMember { get; init; }

    /// <summary>The call is cancelled and reported to the model as <c>timeout</c> after this long.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// True (default) when running the tool twice with the same <see cref="AgentToolContext.IdempotencyKey"/> has the
    /// effect of running it once. Execution is at-least-once: a worker that crashes between the tool's commit and the
    /// step checkpoint runs the tool again. Set false only when that cannot be guaranteed; an interrupted call is then
    /// never repeated and the run stops with <c>tool_outcome_unknown</c> for a human to resolve.
    /// </summary>
    public bool Idempotent { get; init; } = true;

    /// <summary>
    /// The result contains text the platform does not control (a document, an email, a web page, retrieved knowledge).
    /// After such a call every later change in the same run needs a human approval, whatever the autonomy level: this
    /// is the deterministic defence against instructions hidden in content.
    /// </summary>
    public bool ReturnsUntrustedContent { get; init; }

    /// <summary>
    /// Every call needs a human approval of its exact payload, whatever the risk and the autonomy level (e.g. writing
    /// a long-term memory). Sensitive and external tools need one anyway.
    /// </summary>
    public bool RequiresApproval { get; init; }
}

/// <summary>
/// A typed business operation an agent may REQUEST. Implemented by the module that owns the operation.
/// <para><b>Contract:</b></para>
/// <list type="bullet">
/// <item>Call the module's own commands and queries through <c>ISender</c>. A tool is an adapter, not a second
/// business layer: it must not inject a DbContext, a repository or a service provider (architecture test).</item>
/// <item><typeparamref name="TInput"/> holds only what the model may choose. Never a tenant id, a user id or a
/// permission: those come from <see cref="AgentToolContext"/>, which is built by trusted server code.</item>
/// <item>The call runs as the initiating user, inside their tenant and branch scope, exactly like their own request:
/// handlers read the user from <c>IHttpContextService</c> and the tenant from <c>ITenantContext</c> as usual.</item>
/// <item>Throw the usual typed exceptions (<c>NotFoundException</c>, <c>ConflictException</c>, ...). They are returned
/// to the model as an error result with their code; they do not fail the run.</item>
/// <item>Mutations must be idempotent for the same <see cref="AgentToolContext.IdempotencyKey"/> (see
/// <see cref="AgentToolAttribute.Idempotent"/>).</item>
/// <item>Never return secrets. The result is stored with the run and sent to the model provider.</item>
/// </list>
/// An optional FluentValidation <c>IValidator&lt;TInput&gt;</c> registered in the module is run before the tool.
/// </summary>
public interface IAgentTool<in TInput, TOutput>
    where TInput : class
{
    Task<TOutput> ExecuteAsync(TInput input, AgentToolContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Trusted facts about the call, built by the tool gateway from the persisted run. Nothing in it comes from the model.
/// </summary>
/// <param name="TenantId">The tenant the run belongs to (null only in single-tenant deployments without a tenant).</param>
/// <param name="InitiatingUserId">The user the run acts for: the actor of every change the tool makes.</param>
/// <param name="RunId">The agent run.</param>
/// <param name="StepId">The tool-call step.</param>
/// <param name="AgentKey">The agent that requested the call.</param>
/// <param name="CorrelationId">Correlation id of the run (<c>agent-run:&lt;id&gt;</c>), also on its audit entries.</param>
/// <param name="IdempotencyKey">Stable for this tool call across retries: pass it to commands that take one.</param>
/// <param name="Culture">UI culture of the initiating user (<c>en</c> / <c>ar</c>).</param>
public sealed record AgentToolContext(
    Guid? TenantId,
    Guid InitiatingUserId,
    Guid RunId,
    Guid StepId,
    string AgentKey,
    string CorrelationId,
    string IdempotencyKey,
    string Culture);

/// <summary>One registered tool class (the registry reads its <see cref="AgentToolAttribute"/>).</summary>
public sealed record AgentToolRegistration(Type ToolType);

/// <summary>Naming rules shared by the registry, the model gateway and scripted scenarios.</summary>
public static class AgentToolNames
{
    /// <summary>The reserved tool a typed agent calls to hand in its result and finish the run.</summary>
    public const string SubmitResult = "agent.submit_result";

    /// <summary>The reserved tool an agent with <c>Delegates</c> calls to hand a task to another agent (ADR-0033).</summary>
    public const string Delegate = "agent.delegate";

    /// <summary>
    /// Model providers allow only letters, digits, <c>_</c> and <c>-</c> in tool names, so the dot of
    /// <c>module.tool</c> is sent as a double underscore (tool names never contain one themselves).
    /// </summary>
    public static string ToWire(string name) => name.Replace(".", "__", StringComparison.Ordinal);

    public static string FromWire(string wireName) => wireName.Replace("__", ".", StringComparison.Ordinal);
}

public static class AgentToolServiceCollectionExtensions
{
    /// <summary>
    /// Registers a tool (scoped: resolved per call from a fresh unit-of-work scope). Names must be unique across
    /// modules; the registry fails at startup otherwise.
    /// </summary>
    public static IServiceCollection AddAgentTool<TTool>(this IServiceCollection services)
        where TTool : class
    {
        services.AddScoped<TTool>();
        services.AddSingleton(new AgentToolRegistration(typeof(TTool)));
        return services;
    }
}
