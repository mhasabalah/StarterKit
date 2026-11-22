namespace Agentic.Contracts.Conversations;

/// <summary>Lifecycle of a conversation with an agent.</summary>
public enum AgentConversationStatus
{
    [LocalizedDescription("Active", "نشطة")]
    Active = 1,

    /// <summary>Deleted by its owner (or its owner's account was erased): the content of every turn is erased.</summary>
    [LocalizedDescription("Deleted", "محذوفة")]
    Deleted = 2
}

// ----- commands -------------------------------------------------------------------------------------------------

/// <summary>
/// Starts a conversation with a conversational agent (<see cref="AgentDefinition.Conversational"/>) and queues its
/// first turn. Every turn is an ordinary agent run: same permissions, approvals, budgets, quota and durability.
/// </summary>
/// <param name="AgentKey">A registered conversational agent the caller may start.</param>
/// <param name="Message">What the user writes.</param>
/// <param name="InputJson">Optional structured input (a JSON object) for this turn.</param>
/// <param name="Title">Optional title chosen by the user.</param>
public sealed record StartAgentConversationCommand(
    string AgentKey,
    string Message,
    string? InputJson = null,
    string? Title = null) : ICommand<AgentConversationTurnDto>;

/// <summary>
/// Adds a message to the caller's conversation and queues the turn that answers it. Refused with 409
/// <c>conversation_busy</c> while the previous turn is still queued, running or waiting for an approval.
/// </summary>
/// <param name="Version">Optional optimistic-concurrency token of the conversation.</param>
public sealed record SendAgentConversationMessageCommand(
    Guid ConversationId,
    string Message,
    string? InputJson = null,
    long? Version = null) : ICommand<AgentConversationTurnDto>;

/// <param name="Title">The new title; empty clears it.</param>
public sealed record RenameAgentConversationCommand(Guid ConversationId, string? Title, long? Version = null)
    : ICommand<AgentConversationDto>;

/// <summary>
/// Deletes the caller's conversation: a turn in progress is cancelled, and the content of every turn (messages,
/// answers, tool arguments and results) is erased. Counters and the audit trail stay.
/// </summary>
public sealed record DeleteAgentConversationCommand(Guid ConversationId) : ICommand<bool>;

// ----- queries --------------------------------------------------------------------------------------------------

/// <summary>The caller's own active conversations, most recent activity first.</summary>
public sealed record GetAgentConversationsQuery : BaseQuery, IQuery<PaginatedResult<AgentConversationDto>>
{
    public string? AgentKey { get; init; }
}

/// <summary>A conversation with its turns, oldest first.</summary>
public sealed record GetAgentConversationByIdQuery(Guid ConversationId) : IQuery<AgentConversationDetailDto>;

// ----- DTOs -----------------------------------------------------------------------------------------------------

/// <param name="Busy">A turn is queued, running or waiting for an approval: a new message would be refused.</param>
/// <param name="Version">The optimistic-concurrency token.</param>
public sealed record AgentConversationDto(
    Guid Id,
    string AgentKey,
    string? Title,
    AgentConversationStatus Status,
    int MessageCount,
    bool Busy,
    DateTime CreatedAt,
    DateTime LastMessageAt,
    long Version);

/// <summary>One turn: what the user wrote and how the agent answered (the run behind it).</summary>
/// <param name="Message">The user's message; null once the content was erased.</param>
/// <param name="Answer">The agent's answer (free text), or the text of its last model turn for a typed agent.</param>
/// <param name="OutputJson">The typed result of an agent with an output type.</param>
public sealed record AgentConversationMessageDto(
    Guid RunId,
    string? Message,
    string? InputJson,
    string? Answer,
    string? OutputJson,
    AgentRunStatus Status,
    string? FailureCode,
    long InputTokens,
    long OutputTokens,
    DateTime CreatedAt,
    DateTime? FinishedAt);

public sealed record AgentConversationDetailDto(
    AgentConversationDto Conversation,
    IReadOnlyList<AgentConversationMessageDto> Messages);

/// <summary>What starting a conversation or sending a message returns: the conversation and the queued turn.</summary>
public sealed record AgentConversationTurnDto(Guid ConversationId, long ConversationVersion, AgentRunDto Run);
