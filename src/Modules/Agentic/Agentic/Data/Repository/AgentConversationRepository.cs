namespace Agentic.Data.Repository;

/// <summary>Write-side repository of the <see cref="AgentConversation"/> aggregate.</summary>
public interface IAgentConversationRepository
{
    Task<AgentConversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(AgentConversation conversation);

    /// <summary>A turn of the conversation is queued, running or waiting for an approval.</summary>
    Task<bool> HasActiveTurnAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>The turns that are queued, running or waiting for an approval (tracked).</summary>
    Task<List<AgentRun>> GetActiveTurnsAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>An earlier turn read content the platform does not control: the next turn starts tainted.</summary>
    Task<bool> IsTaintedAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>The ids of the conversation's runs, oldest first.</summary>
    Task<List<Guid>> GetRunIdsAsync(Guid conversationId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class AgentConversationRepository(AgenticDbContext context) : IAgentConversationRepository
{
    private static readonly AgentRunStatus[] ActiveStatuses =
        [AgentRunStatus.Queued, AgentRunStatus.Running, AgentRunStatus.AwaitingApproval, AgentRunStatus.AwaitingDelegate];

    public Task<AgentConversation?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Conversations.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(AgentConversation conversation) => context.Conversations.Add(conversation);

    public Task<bool> HasActiveTurnAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        context.Runs.AnyAsync(r => r.ConversationId == conversationId && ActiveStatuses.Contains(r.Status), cancellationToken);

    public Task<List<AgentRun>> GetActiveTurnsAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        context.Runs.Where(r => r.ConversationId == conversationId && ActiveStatuses.Contains(r.Status)).ToListAsync(cancellationToken);

    public Task<bool> IsTaintedAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        context.Runs.AnyAsync(r => r.ConversationId == conversationId && r.Tainted, cancellationToken);

    public Task<List<Guid>> GetRunIdsAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        context.Runs.AsNoTracking().Where(r => r.ConversationId == conversationId)
            .OrderBy(r => r.ConversationTurn).Select(r => r.Id).ToListAsync(cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.IsUniqueViolation(Configurations.AgentRunConfiguration.ActiveTurnIndexName) ||
            ex.IsUniqueViolation(Configurations.AgentRunConfiguration.ConversationTurnIndexName))
        {
            throw new AgentConversationBusyException();
        }
    }
}
