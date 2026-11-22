using System.Text.Json;

namespace Agentic.Runtime.Models;

// The runtime's own, provider-neutral view of a model exchange. The executor works only with these types; the
// Microsoft.Extensions.AI types stay inside ChatClientModelGateway and the provider clients.

public enum ModelRole
{
    User,
    Assistant,
    Tool
}

/// <param name="CallId">The provider's id of the call (results are reported under it).</param>
/// <param name="ToolName">The registered tool name (<c>module.tool</c>), not the wire form.</param>
/// <param name="ArgumentsJson">The arguments as the model produced them. Untrusted.</param>
public sealed record ModelToolCall(string CallId, string ToolName, string ArgumentsJson);

public sealed record ModelToolResult(string CallId, string ToolName, string ResultJson);

public sealed record ModelMessage(
    ModelRole Role,
    string? Text = null,
    IReadOnlyList<ModelToolCall>? ToolCalls = null,
    IReadOnlyList<ModelToolResult>? ToolResults = null);

public sealed record ModelToolDeclaration(string Name, string Description, JsonElement Schema);

/// <param name="ModelKey">A key of <c>Agentic:Models</c>.</param>
/// <param name="AgentKey">The agent being run (selects the script of the scripted provider; a span tag otherwise).</param>
/// <param name="Instructions">The system prompt.</param>
/// <param name="HistoryMessages">
/// How many of the leading <paramref name="Messages"/> are earlier turns of the run's conversation (user message and
/// answer pairs). A real model reads them as the conversation; the scripted provider keeps them apart from the run.
/// </param>
public sealed record ModelRequest(
    string ModelKey,
    string AgentKey,
    string Instructions,
    IReadOnlyList<ModelMessage> Messages,
    IReadOnlyList<ModelToolDeclaration> Tools,
    int HistoryMessages = 0);

public sealed record ModelResponse(
    string? Text,
    IReadOnlyList<ModelToolCall> ToolCalls,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCost,
    string? ModelId,
    string? FinishReason);

/// <summary>
/// One request to a configured model. It NEVER executes a tool: tool calls come back as data
/// (<see cref="ModelResponse.ToolCalls"/>) for the run executor to persist and for the tool gateway to authorize.
/// </summary>
public interface IAgentModelGateway
{
    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}

/// <summary>The provider could not answer after its own retries. The attempt fails and is retried with backoff.</summary>
public sealed class ModelProviderException(string modelKey, Exception innerException)
    : Exception($"The model '{modelKey}' could not be reached.", innerException)
{
    public string ModelKey { get; } = modelKey;
}
