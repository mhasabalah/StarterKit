namespace Agentic.Contracts.Memory;

/// <summary>Who a memory is for.</summary>
public enum AgentMemoryScope
{
    /// <summary>Only runs that act for its owner see it.</summary>
    [LocalizedDescription("Personal", "شخصي")]
    User = 1,

    /// <summary>Every run in the tenant sees it. Only holders of <c>agentic.manage</c> write these.</summary>
    [LocalizedDescription("Tenant", "المستأجر")]
    Tenant = 2
}

/// <summary>How a memory was written.</summary>
public enum AgentMemorySource
{
    /// <summary>A person wrote it through the API.</summary>
    [LocalizedDescription("Person", "شخص")]
    Person = 1,

    /// <summary>An agent proposed it with <c>memory.remember</c> and a person approved that exact text.</summary>
    [LocalizedDescription("Agent (approved)", "وكيل (بموافقة)")]
    Agent = 2
}

/// <summary>
/// A fact agents may recall later (a preference, a standing instruction). Memory is conservative by design: it is
/// written only by a person or through an approved tool call, it is recalled only through the read tool
/// <c>memory.recall</c> (so it is data in the transcript, never part of the instructions), and it expires.
/// </summary>
public sealed record AgentMemoryDto(
    Guid Id,
    AgentMemoryScope Scope,
    Guid? OwnerUserId,
    string Content,
    AgentMemorySource Source,
    Guid? SourceRunId,
    Guid? CreatedBy,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

/// <summary>
/// The caller's personal memories and the tenant's memories that have not expired, newest first, one page at a time.
/// Writing and deleting memories is not part of the contract on purpose: only a person (the module's own endpoints)
/// or an approved <c>memory.remember</c> call writes one, never another module's code that an agent drives.
/// </summary>
public sealed record GetAgentMemoriesQuery(AgentMemoryScope? Scope = null, int? PageNumber = null, int? PageSize = null)
    : IQuery<PaginatedResult<AgentMemoryDto>>;
