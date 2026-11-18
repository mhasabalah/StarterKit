using Agentic.Contracts.Approvals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.Approvals.Features;

/// <param name="PayloadHash">The hash of the action as it was shown to the approver (<c>AgentApprovalDto.PayloadHash</c>).</param>
/// <param name="Note">Optional note kept with the decision.</param>
/// <param name="Version">Optional optimistic-concurrency token of the request.</param>
public sealed record ApproveAgentActionRequest(string PayloadHash, string? Note = null, long? Version = null);

/// <param name="Reason">Optional reason; the agent is told it.</param>
/// <param name="Version">Optional optimistic-concurrency token of the request.</param>
public sealed record RejectAgentActionRequest(string? Reason = null, long? Version = null);

/// <summary>
/// Human approval of actions agents propose (<c>/api/v1/agent-approvals</c>). A run that proposes a sensitive or
/// external action, or any change after it read untrusted content, is suspended until someone decides here.
/// Decisions need an interactive user: no API key, no impersonation. See wiki/Modules/Agentic.md.
/// </summary>
public class AgentApprovalsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/agent-approvals")
            .WithTags("Agents")
            .WithApiVersionSet(app.NewApiVersionSet("AgentApprovals").Build())
            .HasApiVersion(1.0)
            .AddEndpointFilter(RequireEnabled);

        group.MapGet("/", async ([AsParameters] GetAgentApprovalsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetAgentApprovals").Produces<ApiResponse<PaginatedResult<AgentApprovalDto>>>()
            .WithSummary("List approval requests")
            .WithDescription(
                "Newest first. Every request of the tenant with `agentic.approvals.decide`; otherwise the requests of the " +
                "caller's own runs. Filters: status, runId.")
            .RequireTenantMember();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetAgentApprovalByIdQuery(id), ct))))
            .WithName("GetAgentApprovalById").Produces<ApiResponse<AgentApprovalDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Approval request")
            .WithDescription("The proposed action: tool, risk, the exact arguments and the hash that identifies them.")
            .RequireTenantMember();

        group.MapPost("/{id:guid}/approve",
                async (Guid id, ApproveAgentActionRequest request, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                        new ApproveAgentActionCommand(id, request.PayloadHash, request.Note, request.Version), ct))))
            .WithName("ApproveAgentAction").Produces<ApiResponse<AgentApprovalDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Approve a proposed action")
            .WithDescription(
                "Approves exactly the payload identified by `payloadHash` (409 `approval_payload_mismatch` otherwise). " +
                "You must also hold the permission the action itself needs (403 `approver_lacks_permission`). The run is " +
                "queued again and executes the action once.")
            .RequireTenantPermission(TenantPermissions.AgentApprovalsDecide)
            .DisallowImpersonation()
            .DisallowApiKey();

        group.MapPost("/{id:guid}/reject",
                async (Guid id, RejectAgentActionRequest? request, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(
                        new RejectAgentActionCommand(id, request?.Reason, request?.Version), ct))))
            .WithName("RejectAgentAction").Produces<ApiResponse<AgentApprovalDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Reject a proposed action")
            .WithDescription("The action is not performed. The run continues and the agent is told the reason.")
            .RequireTenantPermission(TenantPermissions.AgentApprovalsDecide)
            .DisallowImpersonation()
            .DisallowApiKey();
    }

    /// <summary>While the module is switched off its routes do not exist (404), whoever asks.</summary>
    private static async ValueTask<object?> RequireEnabled(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!context.HttpContext.RequestServices.GetRequiredService<IOptions<AgenticOptions>>().Value.Enabled)
            throw new AgenticDisabledException();
        return await next(context);
    }
}
