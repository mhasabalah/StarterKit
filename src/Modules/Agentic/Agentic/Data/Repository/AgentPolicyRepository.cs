using Agentic.Data.Configurations;

namespace Agentic.Data.Repository;

/// <summary>Write-side repository of the <see cref="AgentPolicy"/> aggregate.</summary>
public interface IAgentPolicyRepository
{
    Task<AgentPolicy?> GetByAgentKeyAsync(string agentKey, CancellationToken cancellationToken = default);

    void Add(AgentPolicy policy);

    /// <summary>Saves; a second policy for the same agent (two first saves at once) becomes a 409.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class AgentPolicyRepository(AgenticDbContext context) : IAgentPolicyRepository
{
    public Task<AgentPolicy?> GetByAgentKeyAsync(string agentKey, CancellationToken cancellationToken = default) =>
        context.Policies.FirstOrDefaultAsync(p => p.AgentKey == agentKey, cancellationToken);

    public void Add(AgentPolicy policy) => context.Policies.Add(policy);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(AgentPolicyConfiguration.AgentIndexName))
        {
            throw new ConcurrencyConflictException(ex);
        }
    }
}
