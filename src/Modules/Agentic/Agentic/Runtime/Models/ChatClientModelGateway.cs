using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Agentic.Runtime.Models;

/// <summary>
/// <see cref="IAgentModelGateway"/> over <see cref="IChatClient"/> (Microsoft.Extensions.AI): the one seam every
/// provider sits behind. Tools are sent as DECLARATIONS only (name, description, schema), so no client can invoke
/// anything, and the function-invoking middleware is deliberately never added.
/// </summary>
public sealed class ChatClientModelGateway(AgentChatClientFactory clients, IOptions<AgenticOptions> options)
    : IAgentModelGateway
{
    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        if (!options.Value.Models.TryGetValue(request.ModelKey, out var model))
            throw new InvalidOperationException($"Agentic:Models has no model '{request.ModelKey}'.");

        var client = clients.Get(request.ModelKey);
        var chatOptions = new ChatOptions
        {
            ModelId = model.ModelId,
            MaxOutputTokens = model.MaxOutputTokens,
            Tools = request.Tools
                .Select(t => (AITool)AIFunctionFactory.CreateDeclaration(AgentToolNames.ToWire(t.Name), t.Description, t.Schema))
                .ToList(),
            AdditionalProperties = new AdditionalPropertiesDictionary { [ScriptedChatClient.AgentKeyProperty] = request.AgentKey }
        };
        if (chatOptions.Tools.Count == 0)
            chatOptions.Tools = null;

        ChatResponse response;
        try
        {
            response = await client.GetResponseAsync(ToChatMessages(request), chatOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Provider SDKs throw their own exception types (HTTP status, rate limit, timeout). They are all the same
            // to the run: this attempt did not get an answer.
            throw new ModelProviderException(request.ModelKey, ex);
        }

        var calls = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Select(c => new ModelToolCall(c.CallId, AgentToolNames.FromWire(c.Name), SerializeArguments(c.Arguments)))
            .ToList();

        var inputTokens = response.Usage?.InputTokenCount ?? 0;
        var outputTokens = response.Usage?.OutputTokenCount ?? 0;
        var cost = (inputTokens * model.InputPricePerMillionTokens + outputTokens * model.OutputPricePerMillionTokens) / 1_000_000m;

        return new ModelResponse(
            string.IsNullOrWhiteSpace(response.Text) ? null : response.Text,
            calls,
            inputTokens,
            outputTokens,
            cost,
            response.ModelId ?? model.ModelId,
            response.FinishReason?.Value);
    }

    private static List<ChatMessage> ToChatMessages(ModelRequest request)
    {
        var messages = new List<ChatMessage>(request.Messages.Count + 1)
        {
            new(ChatRole.System, request.Instructions)
        };

        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case ModelRole.User:
                    messages.Add(new ChatMessage(ChatRole.User, message.Text ?? string.Empty));
                    break;

                case ModelRole.Assistant:
                    var contents = new List<AIContent>();
                    if (!string.IsNullOrEmpty(message.Text))
                        contents.Add(new TextContent(message.Text));
                    foreach (var call in message.ToolCalls ?? [])
                    {
                        contents.Add(new FunctionCallContent(call.CallId, AgentToolNames.ToWire(call.ToolName),
                            ParseArguments(call.ArgumentsJson)));
                    }

                    messages.Add(new ChatMessage(ChatRole.Assistant, contents));
                    break;

                case ModelRole.Tool:
                    messages.Add(new ChatMessage(ChatRole.Tool,
                        (message.ToolResults ?? []).Select(AIContent (r) => new FunctionResultContent(r.CallId, ParseResult(r.ResultJson)))
                        .ToList()));
                    break;
            }
        }

        return messages;
    }

    private static string SerializeArguments(IDictionary<string, object?>? arguments) =>
        arguments is null ? "{}" : JsonSerializer.Serialize(arguments, AgentJson.Output);

    /// <summary>
    /// Arguments of an earlier call, replayed to the model. They are untrusted and may not have been a JSON object;
    /// what was not is replayed as a single raw value so the conversation stays faithful to what happened.
    /// </summary>
    internal static Dictionary<string, object?> ParseArguments(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document.RootElement.EnumerateObject()
                    .ToDictionary(p => p.Name, object? (p) => p.Value.Clone(), StringComparer.Ordinal);
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal) { ["_raw"] = argumentsJson };
    }

    private static object ParseResult(string resultJson)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return resultJson;
        }
    }
}
