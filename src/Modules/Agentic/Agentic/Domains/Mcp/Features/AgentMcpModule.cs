using System.Text.Json;
using Agentic.Runtime.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.Mcp.Features;

/// <summary>
/// The MCP server of agents with <c>McpServer = true</c> (ADR-0034): Streamable HTTP at <c>/api/v1/mcp/{agentKey}</c>,
/// stateless (any replica answers any request). Mapped only while <c>Agentic:Mcp:Enabled</c>; signed in with a JWT or an
/// API key like any endpoint, as a tenant member who may start agent runs, never while impersonating. What a client may
/// call is decided per request by <see cref="AgentMcpGateway"/>.
/// </summary>
public class AgentMcpModule : ICarterModule
{
    public const string Route = EndpointConfig.BaseApiPath + "/mcp/{agentKey}";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var settings = app.ServiceProvider.GetRequiredService<IOptions<AgenticOptions>>().Value;
        if (!settings.Mcp.Enabled)
            return;

        app.MapMcp(Route)
            .WithTags("Agents")
            .RequireTenantPermission(TenantPermissions.AgentRunsStart)
            .RequireFeature(EntitlementKeys.Agents)
            .DisallowImpersonation();
    }
}

/// <summary>The MCP protocol handlers: they only translate between MCP and <see cref="AgentMcpGateway"/>.</summary>
public static class AgentMcpHandlers
{
    public static async ValueTask<ListToolsResult> ListToolsAsync(RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        var services = request.Services ?? throw new InvalidOperationException("The MCP request has no services.");
        var tools = await services.GetRequiredService<AgentMcpGateway>().ListAsync(AgentKeyOf(services), cancellationToken)
                    ?? throw new McpProtocolException(AgenticText.Get("Agent.Mcp.agent_not_found"), McpErrorCode.InvalidRequest);
        return new ListToolsResult
        {
            Tools = tools.Select(t => new Tool
            {
                Name = t.Name,
                Description = t.Description,
                InputSchema = t.InputSchema,
                Annotations = new ToolAnnotations { ReadOnlyHint = t.ReadOnly, DestructiveHint = !t.ReadOnly }
            }).ToList()
        };
    }

    public static async ValueTask<CallToolResult> CallToolAsync(RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        var services = request.Services ?? throw new InvalidOperationException("The MCP request has no services.");
        var call = request.Params ?? throw new McpProtocolException("Missing parameters.", McpErrorCode.InvalidParams);
        var arguments = JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, JsonElement>(), AgentJson.Output);
        McpToolResult result;
        try
        {
            result = await services.GetRequiredService<AgentMcpGateway>()
                .CallAsync(AgentKeyOf(services), call.Name, arguments, cancellationToken);
        }
        catch (AgentNotFoundException)
        {
            throw new McpProtocolException(AgenticText.Get("Agent.Mcp.agent_not_found"), McpErrorCode.InvalidRequest);
        }

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = result.Json }],
            IsError = result.IsError
        };
    }

    private static string AgentKeyOf(IServiceProvider services) =>
        services.GetRequiredService<IHttpContextAccessor>().HttpContext?.GetRouteValue("agentKey") as string
        ?? throw new McpProtocolException(AgenticText.Get("Agent.Mcp.agent_not_found"), McpErrorCode.InvalidRequest);
}
