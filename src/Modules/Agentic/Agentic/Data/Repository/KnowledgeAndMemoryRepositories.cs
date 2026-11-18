using Agentic.Contracts.Memory;
using Agentic.Data.Configurations;

namespace Agentic.Data.Repository;

/// <summary>Write-side repository of <see cref="KnowledgeDocument"/>.</summary>
public interface IKnowledgeDocumentRepository
{
    Task<KnowledgeDocument?> FindAsync(string sourceKey, string externalRef, CancellationToken cancellationToken = default);

    void Add(KnowledgeDocument document);

    void Remove(KnowledgeDocument document);

    /// <summary>Saves; a second document for the same source and owner id (two first saves at once) becomes a 409.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class KnowledgeDocumentRepository(AgenticDbContext context) : IKnowledgeDocumentRepository
{
    public Task<KnowledgeDocument?> FindAsync(string sourceKey, string externalRef, CancellationToken cancellationToken = default) =>
        context.KnowledgeDocuments.FirstOrDefaultAsync(d => d.SourceKey == sourceKey && d.ExternalRef == externalRef,
            cancellationToken);

    public void Add(KnowledgeDocument document) => context.KnowledgeDocuments.Add(document);

    // The passages go with it: the foreign key cascades in the database.
    public void Remove(KnowledgeDocument document) => context.KnowledgeDocuments.Remove(document);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(KnowledgeDocumentConfiguration.ExternalRefIndexName))
        {
            throw new ConcurrencyConflictException(ex);
        }
    }
}

/// <summary>Write-side repository of <see cref="AgentMemory"/>.</summary>
public interface IAgentMemoryRepository
{
    Task<AgentMemory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AgentMemory?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Live (not expired) memories of one owner, or of the tenant when <paramref name="ownerUserId"/> is null.</summary>
    Task<int> CountAsync(AgentMemoryScope scope, Guid? ownerUserId, DateTime now, CancellationToken cancellationToken = default);

    void Add(AgentMemory memory);

    void Remove(AgentMemory memory);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class AgentMemoryRepository(AgenticDbContext context) : IAgentMemoryRepository
{
    public Task<AgentMemory?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Memories.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<AgentMemory?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) =>
        context.Memories.FirstOrDefaultAsync(m => m.IdempotencyKey == key, cancellationToken);

    public Task<int> CountAsync(AgentMemoryScope scope, Guid? ownerUserId, DateTime now, CancellationToken cancellationToken = default) =>
        context.Memories.CountAsync(m => m.Scope == scope && m.OwnerUserId == ownerUserId &&
                                         (m.ExpiresAt == null || m.ExpiresAt > now), cancellationToken);

    public void Add(AgentMemory memory) => context.Memories.Add(memory);

    public void Remove(AgentMemory memory) => context.Memories.Remove(memory);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(AgentMemoryConfiguration.IdempotencyIndexName))
        {
            throw new ConcurrencyConflictException(ex);
        }
    }
}
