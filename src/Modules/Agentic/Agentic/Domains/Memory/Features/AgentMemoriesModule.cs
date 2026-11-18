using Agentic.Contracts.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.Memory.Features;

public sealed record CreateAgentMemoryRequest(string Content, AgentMemoryScope Scope = AgentMemoryScope.User);

/// <summary>
/// What agents may recall (<c>/api/v1/agent-memories</c>): the caller's personal memories and the tenant's. People
/// write memories here; agents only through the approval-gated <c>memory.remember</c> tool. See wiki/Modules/Agentic.md.
/// </summary>
public class AgentMemoriesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versions = app.NewApiVersionSet("AgentMemories").Build();

        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/agent-memories")
            .WithTags("Agents")
            .WithApiVersionSet(versions)
            .HasApiVersion(1.0)
            .AddEndpointFilter(RequireEnabled);

        group.MapGet("/", async ([AsParameters] GetAgentMemoriesQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetAgentMemories").Produces<ApiResponse<PaginatedResult<AgentMemoryDto>>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .WithSummary("Agent memories")
            .WithDescription("Your personal memories and the tenant's memories that have not expired, newest first (paged; filter: scope).")
            .RequireTenantPermission(TenantPermissions.AgentRunsStart);

        group.MapPost("/", async (CreateAgentMemoryRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(
                    await sender.Send(new CreateAgentMemoryCommand(request.Content, request.Scope), ct))))
            .WithName("CreateAgentMemory").Produces<ApiResponse<AgentMemoryDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Write a memory")
            .WithDescription(
                "A personal memory, or a tenant memory (needs `agentic.manage`, 403 `memory_tenant_scope_forbidden` " +
                "otherwise). 409 `memory_limit_reached` when the limit is reached.")
            .RequireTenantPermission(TenantPermissions.AgentRunsStart)
            .DisallowImpersonation();

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteAgentMemoryCommand(id), ct))))
            .WithName("DeleteAgentMemory").Produces<ApiResponse<bool>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete a memory")
            .WithDescription("One of your memories, or a tenant memory with `agentic.manage`.")
            .RequireTenantPermission(TenantPermissions.AgentRunsStart)
            .DisallowImpersonation();
    }

    private static async ValueTask<object?> RequireEnabled(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<AgenticOptions>>().Value.Enabled)
            throw new AgenticDisabledException();
        return await next(context);
    }
}
