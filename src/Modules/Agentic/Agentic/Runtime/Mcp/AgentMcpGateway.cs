using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Agentic.Domains.AgentRuns.Services;
using Agentic.Runtime.Security;
using Agentic.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agentic.Runtime.Mcp;

/// <summary>A tool an MCP client may call: the registered tool as the gateway exposes it.</summary>
public sealed record McpTool(string Name, string Description, JsonElement InputSchema, bool ReadOnly);

/// <summary>The answer to one MCP tool call: the tool's JSON result, or an error the client's model can act on.</summary>
public sealed record McpToolResult(string Json, bool IsError, Guid? RunId);

/// <summary>
/// The tool gateway for external MCP clients (ADR-0034). An MCP client is a model we do not control acting for the
/// person (or API key) that signed in, so every call is decided exactly as a call in a run whose content is untrusted:
/// <list type="bullet">
/// <item>only the tools of an agent with <c>McpServer = true</c> (its allowlist), when the person may start that agent;</item>
/// <item>the person's CURRENT permissions (narrowed by an API key), the agent's autonomy tightened by the tenant policy;</item>
/// <item>tainted: any change needs a person, which a synchronous call cannot get, so it is refused. In practice MCP
/// clients get the read-only tools.</item>
/// </list>
/// Every call that the gateway lets through is recorded as a run with trigger <c>Mcp</c> and one tool step (audit,
/// retention, erasure, the <c>agent_runs</c> quota and capacity), executed in this request inside the person's tenant and
/// branch scope.
/// </summary>
public sealed class AgentMcpGateway(
    AgentRegistry registry,
    AgentAccess access,
    AgentPrincipalResolver principals,
    AgenticDbContext dbContext,
    IUnitOfWorkRunner unitOfWork,
    ITenantContext tenantContext,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    AgenticMetrics metrics,
    ILogger<AgentMcpGateway> logger)
{
    private AgenticOptions Settings => options.Value;
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record Caller(RegisteredAgent Agent, AgentPrincipal Principal, AgentAutonomy Autonomy);

    /// <summary>The tools this caller may call now; null when the agent offers no MCP server to them.</summary>
    public async Task<IReadOnlyList<McpTool>?> ListAsync(string agentKey, CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(agentKey, cancellationToken);
        return caller?.Agent.Tools
            .Where(t => Decide(caller, t).Kind == ToolDecisionKind.Execute)
            .Select(t => new McpTool(t.Name, t.Description, t.InputSchema, !t.IsMutation))
            .ToList();
    }

    public async Task<McpToolResult> CallAsync(string agentKey, string toolName, string argumentsJson,
        CancellationToken cancellationToken)
    {
        var caller = await CallerAsync(agentKey, cancellationToken)
                     ?? throw new AgentNotFoundException(agentKey);
        var tool = registry.FindTool(toolName);
        if (tool is null || !caller.Agent.Allows(tool.Name))
            return Refused(toolName, "tool_not_found", AgenticText.Get("Agent.Mcp.tool_not_found"));

        var decision = Decide(caller, tool);
        if (decision.Kind == ToolDecisionKind.RequireApproval)
            return Refused(tool.Name, "approval_required", AgenticText.Get("Agent.Mcp.approval_required"));
        if (decision.Kind == ToolDecisionKind.Refuse)
            return Refused(tool.Name, decision.Code!, AgenticText.GetOrDefault("Agent.Tool." + decision.Code, "Agent.Tool.refused"));

        if (Encoding.UTF8.GetByteCount(argumentsJson) > Settings.Limits.MaxToolArgumentsBytes)
            return Refused(tool.Name, "arguments_too_large", AgenticText.GetOrDefault("Agent.Tool.arguments_too_large", "Agent.Tool.refused"));

        var check = await InScopeAsync(caller.Principal, null, (services, ct) => tool.Invoker.CheckAsync(services, argumentsJson, ct),
            cancellationToken);
        if (!check.IsValid)
            return Refused(tool.Name, "invalid_arguments", check.Message ?? AgenticText.Get("Agent.Tool.invalid_arguments"));

        // The audit run: started like any run (policy, capacity, quota), never queued, executed here.
        var (run, step) = await unitOfWork.RunAsync(async (services, ct) =>
        {
            var started = await services.GetRequiredService<AgentRunStarter>().StartAsync(new AgentRunStart(caller.Agent,
                AgentTriggerKind.Mcp, access.UserId, $"MCP call: {tool.Name}", argumentsJson, AgenticText.CurrentCulture,
                access.PermissionCeiling, access.ApiKeyId, StartTainted: true, Enqueue: false), ct);
            var repository = services.GetRequiredService<IAgentRunRepository>();
            var tracked = await repository.GetByIdAsync(started.Id, ct) ?? started;
            tracked.TryClaim($"mcp:{Environment.MachineName}", Now, TimeSpan.FromSeconds(Settings.LeaseSeconds));
            var planned = tracked.PlanExternalToolCall($"mcp_{Guid.NewGuid():N}", tool.Name, argumentsJson);
            planned.MarkExecuting(tool.Version, Now);
            repository.AddSteps([planned]);
            await repository.SaveChangesAsync(ct);
            return (tracked, planned);
        }, cancellationToken);

        string resultJson;
        string? errorCode = null;
        var started = timeProvider.GetTimestamp();
        using (var activity = AgenticTelemetry.StartTool(run, step))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(tool.Timeout);
            var context = new AgentToolContext(tenantContext.TenantId, run.InitiatedBy, run.Id, step.Id, run.AgentKey,
                run.CorrelationId, $"{run.Id:N}:{step.Id:N}", run.Culture);
            try
            {
                resultJson = await InScopeAsync(caller.Principal, run.Id,
                    (services, ct) => tool.Invoker.InvokeAsync(services, argumentsJson, context, ct), timeout.Token);
                if (Encoding.UTF8.GetByteCount(resultJson) > Settings.Limits.MaxToolResultBytes)
                {
                    errorCode = "result_too_large";
                    resultJson = AgentJson.Error(errorCode, AgenticText.Get("Agent.Tool.result_too_large"));
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                errorCode = "timeout";
                resultJson = AgentJson.Error(errorCode, AgenticText.Get("Agent.Tool.timeout"));
            }
            catch (InvalidToolArgumentsException ex)
            {
                errorCode = "invalid_arguments";
                resultJson = AgentJson.Error(errorCode, ex.Message);
            }
            catch (ValidationException ex)
            {
                errorCode = "validation_failed";
                resultJson = AgentJson.Error(errorCode, string.Join(" ", ex.Errors.Select(e => e.ErrorMessage)));
            }
            catch (CustomException ex) when ((int)ex.StatusCode is >= 400 and < 500)
            {
                errorCode = ex.ErrorCode ?? "bad_request";
                resultJson = AgentJson.Error(errorCode, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "MCP call {RunId}: the tool {Tool} failed", run.Id, tool.Name);
                errorCode = "tool_failed";
                resultJson = AgentJson.Error(errorCode, AgenticText.Get("Agent.Failure.internal_error"));
            }

            activity?.SetTag("agent.tool.outcome", errorCode ?? "ok");
            if (errorCode is not null)
                activity?.SetStatus(ActivityStatusCode.Error, errorCode);
        }

        var elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        await unitOfWork.RunAsync(async (services, ct) =>
        {
            var repository = services.GetRequiredService<IAgentRunRepository>();
            var current = await repository.GetByIdAsync(run.Id, ct) ?? throw new InvalidOperationException($"Run {run.Id} is gone.");
            var currentStep = await repository.GetStepAsync(run.Id, step.Id, ct)
                              ?? throw new InvalidOperationException($"Run {run.Id} has no step {step.Id}.");
            if (current.IsTerminal)
                return true; // cancelled meanwhile: the outcome is not recorded over the cancellation
            if (errorCode is null)
                currentStep.Succeed(resultJson, elapsedMs, Now);
            else
                currentStep.FailWith(errorCode, resultJson, elapsedMs, Now);
            current.RecordToolCall(currentStep, executed: true, untrustedContent: tool.ReturnsUntrustedContent);
            if (errorCode == "tool_failed")
                current.Fail("tool_failed", AgenticText.Get("Agent.Failure.internal_error"), Now);
            else
                current.Complete(null, null, Now);
            await repository.SaveChangesAsync(ct);
            return true;
        }, CancellationToken.None);

        metrics.RecordToolCall(tool.Name, errorCode ?? "ok");
        logger.LogInformation("MCP call {RunId}: {Tool} for {UserId} ({Outcome})", run.Id, tool.Name, run.InitiatedBy,
            errorCode ?? "ok");
        return new McpToolResult(resultJson, errorCode is not null, run.Id);
    }

    /// <summary>The agent and the caller, or null when the agent offers no MCP server to this caller.</summary>
    private async Task<Caller?> CallerAsync(string agentKey, CancellationToken cancellationToken)
    {
        if (!Settings.Enabled || !Settings.Mcp.Enabled || registry.FindAgent(agentKey) is not { Definition.McpServer: true } agent)
            return null;
        if (!await access.CanStartAsync(agent.Definition))
            return null;

        var policy = (await dbContext.Policies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.AgentKey == agentKey, cancellationToken))?.Settings ?? AgentPolicySettings.Default;
        if (!policy.Enabled)
            return null;

        var principal = await principals.ResolveAsync(access.UserId, access.PermissionCeiling, cancellationToken);
        if (principal is null)
            return null;

        var autonomy = policy.AutonomyCeiling is { } ceiling && ceiling < agent.Definition.Autonomy ? ceiling : agent.Definition.Autonomy;
        return new Caller(agent, principal, autonomy);
    }

    private static ToolDecision Decide(Caller caller, AgentToolDescriptor tool) =>
        ToolPolicy.Decide(caller.Agent, tool, caller.Autonomy, tainted: true, caller.Principal.Permissions);

    private McpToolResult Refused(string tool, string code, string message)
    {
        metrics.RecordToolCall(registry.FindTool(tool)?.Name ?? "unknown", code);
        return new McpToolResult(AgentJson.Error(code, message), true, null);
    }

    /// <summary>As a request of the person: their tenant, CURRENT membership and branch scope, them as the acting user.</summary>
    private async Task<T> InScopeAsync<T>(AgentPrincipal principal, Guid? runId,
        Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        using var scope = principal.Membership is { } membership && tenantContext.Tenant is { } tenant
            ? TenantScope.Begin(tenant, membership: membership)
            : null;
        using var actingUser = ActingUser.Begin(access.UserId, runId is null ? null : AgentRun.CorrelationIdOf(runId.Value));
        return await unitOfWork.RunAsync((services, ct) =>
        {
            services.GetRequiredService<AgentCallScope>().Enter(principal, runId ?? Guid.Empty);
            return work(services, ct);
        }, cancellationToken);
    }
}
