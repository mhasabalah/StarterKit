using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Agentic.Runtime.Models;

/// <summary>
/// <see cref="IAgentModelGateway"/> over <see cref="IChatClient"/> (Microsoft.Extensions.AI): the one seam every
/// provider sits behind. Tools are sent as DECLARATIONS only (name, description, schema), so no client can invoke
/// anything, and the function-invoking middleware is deliberately never added.
/// <para>
/// <b>Streaming.</b> With a text sink and streaming on for the model, the request is streamed: text deltas go to the
/// sink as they arrive, and the updates are assembled into the same <see cref="ChatResponse"/> a non-streamed request
/// returns (text, tool calls, usage, finish reason). A provider that reports no usage while streaming would make the
/// turn look free, so its tokens are estimated for that turn and the model is no longer streamed by this process.
/// </para>
/// </summary>
public sealed class ChatClientModelGateway(
    AgentChatClientFactory clients,
    IOptions<AgenticOptions> options,
    ILogger<ChatClientModelGateway>? logger = null)
    : IAgentModelGateway
{
    private readonly ILogger _logger = logger ?? NullLogger<ChatClientModelGateway>.Instance;

    /// <summary>Models whose provider sent no usage while streaming: they are no longer streamed.</summary>
    private readonly ConcurrentDictionary<string, bool> _noStreamingUsage = new(StringComparer.OrdinalIgnoreCase);

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken) =>
        CompleteAsync(request, null, cancellationToken);

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, IModelTextSink? textSink,
        CancellationToken cancellationToken)
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
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ScriptedChatClient.AgentKeyProperty] = request.AgentKey,
                [ScriptedChatClient.HistoryMessagesProperty] = request.HistoryMessages
            }
        };
        if (chatOptions.Tools.Count == 0)
            chatOptions.Tools = null;

        var messages = ToChatMessages(request);
        var stream = textSink is not null && options.Value.Streaming.Enabled && model.Streaming &&
                     !_noStreamingUsage.ContainsKey(request.ModelKey);
        ChatResponse response;
        try
        {
            response = stream
                ? await StreamAsync(client, messages, chatOptions, textSink!, cancellationToken)
                : await client.GetResponseAsync(messages, chatOptions, cancellationToken);
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

        if (stream && response.Usage is null)
        {
            _noStreamingUsage[request.ModelKey] = true;
            _logger.LogWarning("The model {Model} reported no token usage while streaming; its tokens are estimated for " +
                               "this turn and it is no longer streamed", request.ModelKey);
            response.Usage = new UsageDetails
            {
                InputTokenCount = EstimateTokens(messages.Sum(m => m.Text.Length)),
                OutputTokenCount = EstimateTokens(response.Text.Length +
                                                  response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>()
                                                      .Sum(f => SerializeArguments(f.Arguments).Length))
            };
        }

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

    private static async Task<ChatResponse> StreamAsync(IChatClient client, List<ChatMessage> messages,
        ChatOptions chatOptions, IModelTextSink sink, CancellationToken cancellationToken)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
        {
            updates.Add(update);
            if (update.Text is { Length: > 0 } delta)
                await sink.WriteAsync(delta, cancellationToken);
        }

        return updates.ToChatResponse();
    }

    /// <summary>About four characters per token: only when a provider reports nothing.</summary>
    private static long EstimateTokens(int characters) => characters == 0 ? 0 : (characters + 3) / 4;

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
