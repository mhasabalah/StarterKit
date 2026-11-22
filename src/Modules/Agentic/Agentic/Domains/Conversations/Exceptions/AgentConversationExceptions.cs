namespace Agentic.Domains.Conversations.Exceptions;

// Messages come from AgenticResource (en/ar) through AgenticText; the error codes are stable for clients.

/// <summary>Also thrown for a conversation of someone else (no id probing).</summary>
public sealed class AgentConversationNotFoundException(Guid id)
    : NotFoundException(AgenticText.Get("Agent.Conversation.not_found", id));

/// <summary>An operation the conversation's state does not allow (400 <c>invalid_conversation</c>).</summary>
public sealed class InvalidAgentConversationException : BadRequestException
{
    /// <param name="reason">Resx key suffix: <c>Agent.Conversation.&lt;reason&gt;</c>.</param>
    public InvalidAgentConversationException(string reason)
        : base(AgenticText.Get("Agent.Conversation." + reason)) => ErrorCode = reason;
}

/// <summary>The agent does not hold conversations (400 <c>agent_not_conversational</c>).</summary>
public sealed class AgentNotConversationalException : BadRequestException
{
    public AgentNotConversationalException(string agentKey)
        : base(AgenticText.Get("Agent.Conversation.agent_not_conversational", agentKey)) =>
        ErrorCode = "agent_not_conversational";
}

/// <summary>The previous turn is still queued, running or waiting for an approval (409 <c>conversation_busy</c>).</summary>
public sealed class AgentConversationBusyException : ConflictException
{
    public AgentConversationBusyException()
        : base(AgenticText.Get("Agent.Conversation.conversation_busy")) => ErrorCode = "conversation_busy";
}

/// <summary>The conversation has as many messages as allowed (409 <c>conversation_full</c>).</summary>
public sealed class AgentConversationLimitException : ConflictException
{
    public AgentConversationLimitException(string code, int limit)
        : base(AgenticText.Get("Agent.Conversation." + code, limit)) => ErrorCode = code;
}
