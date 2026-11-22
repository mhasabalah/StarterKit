using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Contracts.Scripting;

/// <summary>A tool call the scripted model asks for. <paramref name="Tool"/> is the registered tool name.</summary>
public sealed record ScriptedToolCall(string Tool, string ArgumentsJson);

/// <summary>What the scripted model answers in one turn: text, tool calls, or both.</summary>
public sealed record ScriptedReply(string? Text = null, IReadOnlyList<ScriptedToolCall>? ToolCalls = null)
{
    public static ScriptedReply Say(string text) => new(text);

    public static ScriptedReply Call(string tool, string argumentsJson) => new(null, [new ScriptedToolCall(tool, argumentsJson)]);

    /// <summary>Hands in the typed result of the run (<see cref="AgentToolNames.SubmitResult"/>).</summary>
    public static ScriptedReply Submit(string resultJson) => Call(AgentToolNames.SubmitResult, resultJson);
}

/// <summary>A tool result the run has produced so far, as the model sees it.</summary>
public sealed record ScriptedToolResult(string Tool, string ResultJson);

/// <summary>An earlier turn of the conversation the run belongs to: what the user wrote and what the agent answered.</summary>
public sealed record ScriptedHistoryTurn(string UserMessage, string AssistantAnswer);

/// <param name="AgentKey">The agent being run.</param>
/// <param name="Prompt">The user's prompt (and structured input) of the run.</param>
/// <param name="TurnNumber">1 for the first model turn of this run (earlier conversation turns do not count).</param>
/// <param name="ToolResults">Every tool result of this run so far, oldest first.</param>
/// <param name="History">The earlier turns of the run's conversation, oldest first; empty outside a conversation.</param>
/// <param name="Summary">The summary of turns older than <paramref name="History"/>, when the conversation has one.</param>
public sealed record ScriptedTurn(
    string AgentKey,
    string? Prompt,
    int TurnNumber,
    IReadOnlyList<ScriptedToolResult> ToolResults,
    IReadOnlyList<ScriptedHistoryTurn>? History = null,
    string? Summary = null)
{
    public IReadOnlyList<ScriptedHistoryTurn> EarlierTurns => History ?? [];
}

/// <summary>
/// A deterministic stand-in for a model, used by the <c>Scripted</c> provider: tests, CI, demos and local development
/// without an API key. It is a pure function of the transcript, so a run that is interrupted and resumed gets the same
/// answers again. The scripted provider is refused in Production.
/// </summary>
public interface IAgentScript
{
    string AgentKey { get; }

    ScriptedReply Next(ScriptedTurn turn);
}

public static class AgentScriptServiceCollectionExtensions
{
    public static IServiceCollection AddAgentScript<TScript>(this IServiceCollection services)
        where TScript : class, IAgentScript
    {
        services.AddSingleton<IAgentScript, TScript>();
        return services;
    }
}
