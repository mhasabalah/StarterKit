namespace Agentic.Domains.Conversations.Models;

/// <summary>
/// A conversation between one person and one conversational agent. It holds no message itself: every message is an
/// <see cref="AgentRun"/> linked by <see cref="AgentRun.ConversationId"/>, so each turn keeps all the guarantees of a
/// run (permissions re-checked per tool call, approvals, budgets, quota, durable execution, erasure). The aggregate
/// owns what spans turns: who may continue it, how many messages it has, and that it is still open.
/// <para>One turn at a time: a filtered unique index on <c>agent_runs</c> allows a single queued, running or waiting run
/// per conversation, and <see cref="RecordMessage"/> changes the row so two messages sent at once conflict on
/// <see cref="Entity{T}.Version"/>.</para>
/// </summary>
public class AgentConversation : Aggregate<Guid>, IMustHaveTenant
{
    public const int TitleMaxLength = 120;

    /// <summary>The longest summary the column holds (<c>Conversations:MaxSummaryCharacters</c> is at most this).</summary>
    public const int SummaryMaxLength = 20_000;

    public Guid TenantId { get; set; }

    public string AgentKey { get; private set; } = default!;

    /// <summary>The person who started it; the only one who may continue, rename or delete it.</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>The API key that started it, if one did: only that key (or its owner signed in) may continue it.</summary>
    public Guid? ApiKeyId { get; private set; }

    /// <summary>Chosen by the user; erased with the content.</summary>
    public string? Title { get; private set; }

    public AgentConversationStatus Status { get; private set; }

    public int MessageCount { get; private set; }

    public DateTime LastMessageAt { get; private set; }

    public DateTime? DeletedAt { get; private set; }

    /// <summary>
    /// The model's summary of turns 1..<see cref="SummaryThroughTurn"/> (ADR-0031). Model output, so untrusted; erased
    /// with the conversation's content.
    /// </summary>
    public string? Summary { get; private set; }

    /// <summary>The last turn the summary covers; 0 = no summary.</summary>
    public int SummaryThroughTurn { get; private set; }

    public DateTime? SummaryUpdatedAt { get; private set; }

    /// <summary>Tokens and estimated cost of every summary written for this conversation.</summary>
    public long SummaryInputTokens { get; private set; }

    public long SummaryOutputTokens { get; private set; }

    public decimal SummaryCost { get; private set; }

    private AgentConversation()
    {
    }

    public static AgentConversation Start(string agentKey, Guid ownerId, Guid? apiKeyId, string? title, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(agentKey) || agentKey.Length > AgentRun.AgentKeyMaxLength)
            throw new InvalidAgentConversationException("agent_required");
        if (ownerId == Guid.Empty)
            throw new InvalidAgentConversationException("owner_required");

        var conversation = new AgentConversation
        {
            Id = Guid.CreateVersion7(),
            AgentKey = agentKey,
            OwnerId = ownerId,
            ApiKeyId = apiKeyId,
            Status = AgentConversationStatus.Active,
            LastMessageAt = now
        };
        conversation.Rename(title);
        return conversation;
    }

    /// <summary>Counts a new message (the run that answers it is saved in the same unit of work).</summary>
    public void RecordMessage(DateTime now, int maxMessages)
    {
        EnsureActive();
        if (maxMessages > 0 && MessageCount >= maxMessages)
            throw new AgentConversationLimitException("conversation_full", maxMessages);

        MessageCount++;
        LastMessageAt = now;
    }

    public void Rename(string? title)
    {
        EnsureActive();
        var trimmed = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (trimmed is { Length: > TitleMaxLength })
            throw new InvalidAgentConversationException("title_too_long");
        Title = trimmed;
    }

    /// <summary>
    /// Replaces the summary with one that covers turns 1..<paramref name="throughTurn"/> (it already includes the
    /// previous summary). False when a summary of those turns or later ones already exists: nothing changes.
    /// </summary>
    public bool RecordSummary(string summary, int throughTurn, int maxCharacters, long inputTokens, long outputTokens,
        decimal cost, DateTime now)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(summary))
            throw new InvalidAgentConversationException("summary_empty");
        if (throughTurn < 1 || throughTurn > MessageCount)
            throw new InvalidAgentConversationException("summary_turn_invalid");
        if (throughTurn <= SummaryThroughTurn)
            return false;

        var max = Math.Clamp(maxCharacters, 1, SummaryMaxLength);
        var text = summary.Trim();
        Summary = text.Length <= max ? text : text[..max];
        SummaryThroughTurn = throughTurn;
        SummaryUpdatedAt = now;
        SummaryInputTokens += inputTokens;
        SummaryOutputTokens += outputTokens;
        SummaryCost += cost;
        return true;
    }

    /// <summary>Closes it for good and drops the title and the summary; the caller erases the content of its runs.</summary>
    public void Delete(DateTime now)
    {
        if (Status == AgentConversationStatus.Deleted)
            return;
        Status = AgentConversationStatus.Deleted;
        Title = null;
        DeletedAt = now;
        EraseSummary();
    }

    /// <summary>Drops the summary; the next summary is written again from the turns whose content still exists.</summary>
    public void EraseSummary()
    {
        Summary = null;
        SummaryThroughTurn = 0;
        SummaryUpdatedAt = null;
    }

    /// <summary>Retention: the title is user content, erased with the content of the turns.</summary>
    public void EraseTitle() => Title = null;

    private void EnsureActive()
    {
        if (Status != AgentConversationStatus.Active)
            throw new InvalidAgentConversationException("conversation_deleted");
    }
}
