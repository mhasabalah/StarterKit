using Agentic.Contracts.Memory;

namespace Agentic.Domains.Memory.Models;

/// <summary>
/// A fact agents may recall (see <see cref="AgentMemoryDto"/>). Written by a person, or by an agent through the
/// approval-gated <c>memory.remember</c> tool (then <see cref="Source"/> is <see cref="AgentMemorySource.Agent"/> and
/// <see cref="SourceRunId"/> names the run). Personal memories belong to one user and are erased with them.
/// </summary>
public class AgentMemory : Aggregate<Guid>, IMustHaveTenant
{
    public const int ContentMaxLength = 1_000;
    public const int IdempotencyKeyMaxLength = 100;

    public Guid TenantId { get; set; }

    public AgentMemoryScope Scope { get; private set; }

    /// <summary>The user a personal memory belongs to; null for a tenant memory.</summary>
    public Guid? OwnerUserId { get; private set; }

    public string Content { get; private set; } = default!;

    public AgentMemorySource Source { get; private set; }

    public Guid? SourceRunId { get; private set; }

    /// <summary>The tool call that wrote it (unique per tenant): a repeated call returns the same memory.</summary>
    public string? IdempotencyKey { get; private set; }

    public Guid? AuthorId { get; private set; }

    public DateTime? ExpiresAt { get; private set; }

    public new DateTime CreatedAt { get; private set; }

    private AgentMemory()
    {
    }

    public static AgentMemory Write(AgentMemoryScope scope, Guid authorId, string content, DateTime now,
        TimeSpan? timeToLive, Guid? sourceRunId = null, string? idempotencyKey = null)
    {
        content = content.Trim();
        if (content.Length is 0 or > ContentMaxLength)
            throw new ArgumentException("A memory has 1 to 1000 characters.", nameof(content));

        return new AgentMemory
        {
            Id = Guid.CreateVersion7(),
            Scope = scope,
            OwnerUserId = scope == AgentMemoryScope.User ? authorId : null,
            Content = content,
            Source = sourceRunId is null ? AgentMemorySource.Person : AgentMemorySource.Agent,
            SourceRunId = sourceRunId,
            IdempotencyKey = idempotencyKey,
            AuthorId = authorId,
            CreatedAt = now,
            ExpiresAt = timeToLive is { } ttl ? now + ttl : null
        };
    }

    public bool IsVisibleTo(Guid userId) => Scope == AgentMemoryScope.Tenant || OwnerUserId == userId;
}
