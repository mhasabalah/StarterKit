using Agentic.Contracts.Scripting;
using Agentic.Domains.Conversations.Jobs;

namespace Agentic.Domains.Conversations.Services;

/// <summary>
/// The scripted provider's summary writer (tests, demos, CI): a deterministic summary that keeps every user message of
/// the summarized turns, shortened, so a test can check which turns a summary covers. Refused in Production with the
/// rest of the scripted provider.
/// </summary>
public sealed class ConversationSummaryScript : IAgentScript
{
    public const string Prefix = "Earlier the user asked: ";

    public string AgentKey => SummarizeAgentConversationJob.AgentKey;

    public ScriptedReply Next(ScriptedTurn turn)
    {
        var lines = (turn.Prompt ?? string.Empty).Split('\n');
        var asked = lines
            .Where(l => l.StartsWith("User: ", StringComparison.Ordinal))
            .Select(l => l["User: ".Length..].Trim())
            .Select(l => l.Length <= 60 ? l : l[..60]);
        var previous = lines.SkipWhile(l => l != "Summary so far:").Skip(1).TakeWhile(l => l.Length > 0).FirstOrDefault();
        var summary = Prefix + string.Join(" | ", asked);
        return ScriptedReply.Say(previous is null ? summary : $"{previous} | {summary[Prefix.Length..]}");
    }
}
