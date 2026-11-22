namespace Agentic.Domains.Conversations.Models;

/// <summary>What a model call outside a run was for.</summary>
public enum AgentModelUsageKind
{
    /// <summary><c>SummarizeAgentConversationJob</c> (ADR-0031).</summary>
    ConversationSummary = 1
}

/// <summary>
/// One model call made outside a run (today: a conversation summary). Runs record their own tokens; these rows let
/// <c>GET /agent-usage</c> count every call the tenant's <c>agent_tokens</c> paid for. Written whether or not the result
/// was kept, because the provider was paid either way. Counters only, never content.
/// </summary>
public class AgentModelUsage : Entity<Guid>, IMustHaveTenant
{
    public const int ModelMaxLength = 100;

    public Guid TenantId { get; set; }

    public AgentModelUsageKind Kind { get; private set; }

    /// <summary>The agent the call was made for (a summary: the conversation's agent).</summary>
    public string AgentKey { get; private set; } = default!;

    public Guid? ConversationId { get; private set; }

    public string Model { get; private set; } = default!;

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    public decimal EstimatedCost { get; private set; }

    public new DateTime CreatedAt { get; private set; }

    private AgentModelUsage()
    {
    }

    public static AgentModelUsage ForSummary(string agentKey, Guid conversationId, string model, long inputTokens,
        long outputTokens, decimal estimatedCost, DateTime now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Kind = AgentModelUsageKind.ConversationSummary,
            AgentKey = agentKey,
            ConversationId = conversationId,
            Model = model.Length <= ModelMaxLength ? model : model[..ModelMaxLength],
            InputTokens = Math.Max(0, inputTokens),
            OutputTokens = Math.Max(0, outputTokens),
            EstimatedCost = Math.Max(0, estimatedCost),
            CreatedAt = now
        };
}
