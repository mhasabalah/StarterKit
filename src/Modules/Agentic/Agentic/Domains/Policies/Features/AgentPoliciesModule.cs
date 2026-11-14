using Agentic.Contracts.Policies;
using Agentic.Contracts.Usage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.Policies.Features;

public sealed record UpdateAgentPolicyRequest(
    bool Enabled = true,
    AgentAutonomy? AutonomyCeiling = null,
    bool AllowAutomaticTriggers = true,
    bool RequireApprovalForChanges = false,
    int? MaxModelTurns = null,
    int? MaxToolCalls = null,
    long? MaxTokens = null,
    long? Version = null);

/// <summary>
/// Tenant governance of agents (<c>/api/v1/agent-policies</c>, <c>/api/v1/agent-usage</c>): what each agent may do in
/// this tenant, and what the tenant's runs used. A policy only tightens what the agent's code allows.
/// See wiki/Modules/Agentic.md.
/// </summary>
public class AgentPoliciesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versions = app.NewApiVersionSet("AgentPolicies").Build();

        var policies = app.MapGroup(EndpointConfig.BaseApiPath + "/agent-policies")
            .WithTags("Agents")
            .WithApiVersionSet(versions)
            .HasApiVersion(1.0)
            .AddEndpointFilter(RequireEnabled);

        policies.MapGet("/{agentKey}", async (string agentKey, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetAgentPolicyQuery(agentKey), ct))))
            .WithName("GetAgentPolicy").Produces<ApiResponse<AgentPolicyDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Agent policy")
            .WithDescription("The tenant's policy for an agent (the defaults when none was set).")
            .RequireTenantPermission(TenantPermissions.AgentsManage);

        policies.MapPut("/{agentKey}",
                async (string agentKey, UpdateAgentPolicyRequest request, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new UpdateAgentPolicyCommand(agentKey,
                        request.Enabled, request.AutonomyCeiling, request.AllowAutomaticTriggers,
                        request.RequireApprovalForChanges, request.MaxModelTurns, request.MaxToolCalls, request.MaxTokens,
                        request.Version), ct))))
            .WithName("UpdateAgentPolicy").Produces<ApiResponse<AgentPolicyDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Set an agent policy")
            .WithDescription(
                "Switch an agent off for the tenant, lower its autonomy or budgets, refuse automatic triggers, or demand a " +
                "person for every change. Applies to runs started afterwards.")
            .RequireTenantPermission(TenantPermissions.AgentsManage)
            .DisallowImpersonation();

        app.MapGroup(EndpointConfig.BaseApiPath + "/agent-usage")
            .WithTags("Agents")
            .WithApiVersionSet(versions)
            .HasApiVersion(1.0)
            .AddEndpointFilter(RequireEnabled)
            .MapGet("/", async (DateTime? from, DateTime? to, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetAgentUsageQuery(from, to), ct))))
            .WithName("GetAgentUsage").Produces<ApiResponse<AgentUsageDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .WithSummary("Agent usage")
            .WithDescription(
                "Runs, model turns, tool calls, tokens and estimated cost of the tenant's runs created in [from, to) " +
                "(UTC; default: the current month), in total and per agent.")
            .RequireTenantPermission(TenantPermissions.AgentRunsRead);
    }

    /// <summary>While the module is switched off its routes do not exist (404), whoever asks.</summary>
    private static async ValueTask<object?> RequireEnabled(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<AgenticOptions>>().Value.Enabled)
            throw new AgenticDisabledException();
        return await next(context);
    }
}
