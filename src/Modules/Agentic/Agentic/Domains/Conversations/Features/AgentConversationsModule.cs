using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Web.Idempotency;

namespace Agentic.Domains.Conversations.Features;

/// <param name="AgentKey">A conversational agent the caller may start.</param>
/// <param name="Message">What the user writes.</param>
/// <param name="Input">Optional structured input (a JSON object) for this turn.</param>
/// <param name="Title">Optional title.</param>
public sealed record StartAgentConversationRequest(
    string AgentKey,
    string Message,
    System.Text.Json.JsonElement? Input = null,
    string? Title = null);

/// <param name="Version">Optional optimistic-concurrency token of the conversation.</param>
public sealed record SendAgentConversationMessageRequest(
    string Message,
    System.Text.Json.JsonElement? Input = null,
    long? Version = null);

public sealed record RenameAgentConversationRequest(string? Title, long? Version = null);

/// <summary>
/// Conversations with agents (<c>/api/v1/agent-conversations</c>). Each message queues a turn (an agent run) and returns
/// 202 with it; the turn runs in the background like any run (Hangfire queue <c>agentic</c>, live progress on
/// <c>agentic.run-progress</c> with the <c>conversationId</c>). Read the answer with <c>GET /{id}</c>. Every route
/// answers 404 while <c>Agentic:Enabled</c> is false. See wiki/Modules/Agentic.md#conversations.
/// </summary>
public class AgentConversationsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versions = app.NewApiVersionSet("AgentConversations").Build();
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/agent-conversations")
            .WithTags("Agents")
            .WithApiVersionSet(versions)
            .HasApiVersion(1.0)
            .AddEndpointFilter(RequireEnabled);

        group.MapPost("/", async (StartAgentConversationRequest request, ISender sender, CancellationToken ct) =>
                {
                    var turn = await sender.Send(new StartAgentConversationCommand(request.AgentKey, request.Message,
                        request.Input?.GetRawText(), request.Title), ct);
                    return Results.Accepted($"{EndpointConfig.BaseApiPath}/agent-conversations/{turn.ConversationId}",
                        ApiResponseExtensions.Success(turn));
                })
            .WithName("StartAgentConversation")
            .Produces<ApiResponse<AgentConversationTurnDto>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Start a conversation with an agent")
            .WithDescription(
                "Starts a conversation with a conversational agent and queues the turn that answers the first message. " +
                "The agent acts with the caller's own permissions, re-checked for every tool call. Send an " +
                "`Idempotency-Key` header to make a retried request return the same turn.")
            .RequireTenantPermission(TenantPermissions.AgentRunsStart)
            .RequireFeature(EntitlementKeys.Agents)
            .DisallowImpersonation()
            .RequireRateLimiting("create")
            .WithIdempotency();

        group.MapPost("/{id:guid}/messages",
                async (Guid id, SendAgentConversationMessageRequest request, ISender sender, CancellationToken ct) =>
                {
                    var turn = await sender.Send(new SendAgentConversationMessageCommand(id, request.Message,
                        request.Input?.GetRawText(), request.Version), ct);
                    return Results.Accepted($"{EndpointConfig.BaseApiPath}/agent-runs/{turn.Run.Id}",
                        ApiResponseExtensions.Success(turn));
                })
            .WithName("SendAgentConversationMessage")
            .Produces<ApiResponse<AgentConversationTurnDto>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Send a message")
            .WithDescription(
                "Queues the next turn of your conversation. The agent is given the earlier turns (each message and its " +
                "answer). 409 `conversation_busy` while the previous turn is still running or waiting for an approval; " +
                "409 `conversation_full` when the conversation has as many messages as allowed.")
            .RequireTenantPermission(TenantPermissions.AgentRunsStart)
            .RequireFeature(EntitlementKeys.Agents)
            .DisallowImpersonation()
            .RequireRateLimiting("create")
            .WithIdempotency();

        group.MapGet("/", async ([AsParameters] GetAgentConversationsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetAgentConversations")
            .Produces<ApiResponse<PaginatedResult<AgentConversationDto>>>()
            .WithSummary("My conversations")
            .WithDescription("Your conversations, most recent activity first. Filter: agentKey.")
            .RequireTenantMember();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetAgentConversationByIdQuery(id), ct))))
            .WithName("GetAgentConversationById")
            .Produces<ApiResponse<AgentConversationDetailDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Conversation")
            .WithDescription("The conversation with every turn, oldest first: your own, or any with `agentic.runs.read`.")
            .RequireTenantMember();

        group.MapPatch("/{id:guid}", async (Guid id, RenameAgentConversationRequest request, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(
                    await sender.Send(new RenameAgentConversationCommand(id, request.Title, request.Version), ct))))
            .WithName("RenameAgentConversation")
            .Produces<ApiResponse<AgentConversationDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Rename a conversation")
            .WithDescription("Sets or clears the title of your conversation.")
            .RequireTenantMember()
            .DisallowImpersonation();

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new DeleteAgentConversationCommand(id), ct))))
            .WithName("DeleteAgentConversation")
            .Produces<ApiResponse<bool>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete a conversation")
            .WithDescription(
                "Deletes your conversation: a turn in progress is stopped and the content of every turn (messages, " +
                "answers, tool arguments and results) is erased. Counters and the audit trail stay.")
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
