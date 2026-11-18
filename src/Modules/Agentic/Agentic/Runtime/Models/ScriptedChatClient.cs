using System.Runtime.CompilerServices;
using System.Text.Json;
using Agentic.Contracts.Scripting;
using Microsoft.Extensions.AI;

namespace Agentic.Runtime.Models;

/// <summary>
/// The <c>Scripted</c> provider: an <see cref="IChatClient"/> that answers from the <see cref="IAgentScript"/> of the
/// agent instead of calling a model. It sits behind the same gateway as the real providers, so tests and demos
/// exercise the whole path (message conversion, tool declarations, usage) deterministically and without an API key.
/// Its answer is a pure function of the conversation it is given, so a resumed run gets the same answer again.
/// Refused in Production (<c>AgenticStartupCheck</c>).
/// </summary>
public sealed class ScriptedChatClient(IEnumerable<IAgentScript> scripts) : IChatClient
{
    public const string AgentKeyProperty = "agentic.agent_key";
    public const string ModelId = "scripted";

    private readonly Dictionary<string, IAgentScript> _scripts = Index(scripts);

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var conversation = messages.ToList();
        var agentKey = options?.AdditionalProperties?.TryGetValue(AgentKeyProperty, out var key) == true
            ? key?.ToString() ?? string.Empty
            : string.Empty;

        var turn = ToTurn(agentKey, conversation);
        var reply = _scripts.TryGetValue(agentKey, out var script)
            ? script.Next(turn)
            : ScriptedReply.Say($"No script is registered for the agent '{agentKey}' (AddAgentScript).");

        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(reply.Text))
            contents.Add(new TextContent(reply.Text));
        var index = 0;
        foreach (var call in reply.ToolCalls ?? [])
        {
            contents.Add(new FunctionCallContent($"call_{turn.TurnNumber}_{++index}", AgentToolNames.ToWire(call.Tool),
                ChatClientModelGateway.ParseArguments(call.ArgumentsJson)));
        }

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
        {
            ModelId = ModelId,
            FinishReason = index > 0 ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
            // Deterministic stand-in for token counts (about four characters per token), so budgets and metering can
            // be tested without a provider.
            Usage = new UsageDetails
            {
                InputTokenCount = Estimate(conversation.Sum(m => Length(m.Contents))),
                OutputTokenCount = Estimate(Length(contents))
            }
        };
        response.Usage.TotalTokenCount = response.Usage.InputTokenCount + response.Usage.OutputTokenCount;
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    private static ScriptedTurn ToTurn(string agentKey, List<ChatMessage> conversation)
    {
        var prompt = conversation.FirstOrDefault(m => m.Role == ChatRole.User)?.Text;
        var turnNumber = conversation.Count(m => m.Role == ChatRole.Assistant) + 1;

        var toolByCall = conversation
            .SelectMany(m => m.Contents).OfType<FunctionCallContent>()
            .GroupBy(c => c.CallId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => AgentToolNames.FromWire(g.First().Name), StringComparer.Ordinal);
        var results = conversation
            .Where(m => m.Role == ChatRole.Tool)
            .SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => new ScriptedToolResult(toolByCall.GetValueOrDefault(r.CallId, string.Empty), ToJson(r.Result)))
            .ToList();

        return new ScriptedTurn(agentKey, prompt, turnNumber, results);
    }

    private static string ToJson(object? result) => result switch
    {
        null => "null",
        JsonElement element => element.GetRawText(),
        string text => text,
        _ => JsonSerializer.Serialize(result, AgentJson.Output)
    };

    private static long Length(IEnumerable<AIContent> contents) => contents.Sum(c => c switch
    {
        TextContent text => (long)(text.Text?.Length ?? 0),
        FunctionCallContent call => call.Name.Length + (call.Arguments is null ? 0 : JsonSerializer.Serialize(call.Arguments, AgentJson.Output).Length),
        FunctionResultContent result => ToJson(result.Result).Length,
        _ => 0L
    });

    private static long Estimate(long characters) => (characters + 3) / 4;

    private static Dictionary<string, IAgentScript> Index(IEnumerable<IAgentScript> scripts)
    {
        var index = new Dictionary<string, IAgentScript>(StringComparer.Ordinal);
        foreach (var script in scripts)
        {
            if (!index.TryAdd(script.AgentKey, script))
                throw new InvalidOperationException($"Two agent scripts are registered for '{script.AgentKey}'.");
        }

        return index;
    }
}
