using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Web.Idempotency;

namespace Agentic.Domains.AgentRuns.Features;

/// <param name="AgentKey">A registered agent the caller may start.</param>
/// <param name="Prompt">The goal in natural language.</param>
/// <param name="Input">Optional structured input (a JSON object) given to the agent next to the prompt.</param>
/// <param name="SessionId">Optional: groups the runs of one conversation.</param>
public sealed record StartAgentRunRequest(
    string AgentKey,
    string? Prompt = null,
    System.Text.Json.JsonElement? Input = null,
    Guid? SessionId = null);

/// <param name="Version">Optional optimistic-concurrency token of the run.</param>
public sealed record CancelAgentRunRequest(long? Version = null);

/// <summary>
/// AI agents and their runs (<c>/api/v1/agents</c>, <c>/api/v1/agent-runs</c>). Starting a run returns 202 with the
/// run; it executes in the background (Hangfire queue <c>agentic</c>). Poll <c>GET /agent-runs/{id}</c>: the row is the
/// source of truth. Every route answers 404 while <c>Agentic:Enabled</c> is false. See wiki/Modules/Agentic.md.
/// </summary>
public class AgentRunsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versions = app.NewApiVersionSet("Agents").Build();

        var agents = app.MapGroup(EndpointConfig.BaseApiPath + "/agents")
            .WithTags("Agents")
            .WithApiVersionSet(versions)
            .HasApiVersion(1.0)
            .AddEndpointFilter(RequireEnabled);
        MapAgents(agents);

        var runs = app.MapGroup(EndpointConfig.BaseApiPath + "/agent-runs")
            .WithTags("Agents")
            .WithApiVersionSet(versions)
            .HasApiVersion(1.0)
            .AddEndpointFilter(RequireEnabled);
        MapRuns(runs);
    }

    private static void MapAgents(RouteGroupBuilder group)
    {
        group.MapGet("/", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetAgentsQuery(), ct))))
            .WithName("GetAgents").Produces<ApiResponse<IReadOnlyList<AgentDto>>>()
            .WithSummary("Agents")
            .WithDescription("The agents the caller may start.")
            .RequireTenantMember()
            .RequireFeature(EntitlementKeys.Agents);

        group.MapGet("/{key}", async (string key, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetAgentByKeyQuery(key), ct))))
            .WithName("GetAgentByKey").Produces<ApiResponse<AgentDetailDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Agent")
            .WithDescription("An agent with the tools it may request, their risk and the permission each needs.")
            .RequireTenantMember()
            .RequireFeature(EntitlementKeys.Agents);
    }

    private static void MapRuns(RouteGroupBuilder group)
    {
        group.MapPost("/", async (StartAgentRunRequest request, ISender sender, CancellationToken ct) =>
                {
                    var run = await sender.Send(new StartAgentRunCommand(request.AgentKey, request.Prompt,
                        request.Input?.GetRawText(), request.SessionId), ct);
                    return Results.Accepted($"{EndpointConfig.BaseApiPath}/agent-runs/{run.Id}", ApiResponseExtensions.Success(run));
                })
            .WithName("StartAgentRun")
            .Produces<ApiResponse<AgentRunDto>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Start an agent run")
            .WithDescription(
                "Queues a run of the agent for the caller. The agent acts with the caller's own permissions, re-checked " +
                "for every tool call; with an API key, never beyond the key's scopes. Send an `Idempotency-Key` header to " +
                "make a retried request return the same run.")
            .RequireTenantPermission(TenantPermissions.AgentRunsStart)
            .RequireFeature(EntitlementKeys.Agents)
            .DisallowImpersonation()
            .RequireRateLimiting("create")
            .WithIdempotency();

        group.MapGet("/", async ([AsParameters] GetAgentRunsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetAgentRuns").Produces<ApiResponse<PaginatedResult<AgentRunDto>>>()
            .WithSummary("List agent runs")
            .WithDescription("Newest first: the caller's own runs (every run with `agentic.runs.read`). Filters: agentKey, status.")
            .RequireTenantMember();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetAgentRunByIdQuery(id), ct))))
            .WithName("GetAgentRunById").Produces<ApiResponse<AgentRunDetailDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Agent run")
            .WithDescription("Status, counters, the prompt and the result.")
            .RequireTenantMember();

        group.MapGet("/{id:guid}/steps",
                async (Guid id, int? pageNumber, int? pageSize, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        await sender.Send(new GetAgentRunStepsQuery(id, pageNumber ?? 1, pageSize ?? 50), ct))))
            .WithName("GetAgentRunSteps").Produces<ApiResponse<PaginatedResult<AgentRunStepDto>>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Transcript of an agent run")
            .WithDescription("Every model turn and tool call in order, with what each tool was asked and answered.")
            .RequireTenantMember();

        group.MapPost("/{id:guid}/cancel",
                async (Guid id, CancelAgentRunRequest? request, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new CancelAgentRunCommand(id, request?.Version), ct))))
            .WithName("CancelAgentRun").Produces<ApiResponse<AgentRunDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Cancel an agent run")
            .WithDescription(
                "Your own runs, or any run with `agentic.manage`. A waiting run is cancelled at once; a running one stops " +
                "at its next checkpoint, and what its tools already committed stays committed.")
            .RequireTenantMember()
            .DisallowImpersonation();
    }

    /// <summary>While the module is switched off its routes do not exist (404), whoever asks.</summary>
    private static async ValueTask<object?> RequireEnabled(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<AgenticOptions>>().Value.Enabled)
            throw new AgenticDisabledException();
        return await next(context);
    }
}
