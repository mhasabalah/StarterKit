using Agentic.Contracts.Approvals;

namespace Agentic.Data.Repository;

/// <summary>Write-side repository of the <see cref="AgentApproval"/> aggregate (decision handlers, the expiry job).</summary>
public interface IAgentApprovalRepository
{
    Task<AgentApproval?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The open requests (pending or approved but not used yet) of a run.</summary>
    Task<List<AgentApproval>> GetOpenForRunAsync(Guid runId, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class AgentApprovalRepository(AgenticDbContext context) : IAgentApprovalRepository
{
    public Task<AgentApproval?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Approvals.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<List<AgentApproval>> GetOpenForRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
        context.Approvals
            .Where(a => a.RunId == runId &&
                        (a.Status == AgentApprovalStatus.Pending || a.Status == AgentApprovalStatus.Approved))
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
