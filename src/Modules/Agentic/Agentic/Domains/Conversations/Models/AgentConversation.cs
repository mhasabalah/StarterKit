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

    /// <summary>Closes it for good and drops the title; the caller erases the content of its runs.</summary>
    public void Delete(DateTime now)
    {
        if (Status == AgentConversationStatus.Deleted)
            return;
        Status = AgentConversationStatus.Deleted;
        Title = null;
        DeletedAt = now;
    }

    /// <summary>Retention: the title is user content, erased with the content of the turns.</summary>
    public void EraseTitle() => Title = null;

    private void EnsureActive()
    {
        if (Status != AgentConversationStatus.Active)
            throw new InvalidAgentConversationException("conversation_deleted");
    }
}
