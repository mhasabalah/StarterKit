namespace Agentic.Data.Repository;

/// <summary>Write-side repository of the <see cref="AgentRun"/> aggregate and its steps (command handlers, the executor).</summary>
public interface IAgentRunRepository
{
    Task<AgentRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The whole transcript, in order (tracked).</summary>
    Task<List<AgentRunStep>> GetStepsAsync(Guid runId, CancellationToken cancellationToken = default);

    Task<AgentRunStep?> GetStepAsync(Guid runId, Guid stepId, CancellationToken cancellationToken = default);

    Task AddAsync(AgentRun run, CancellationToken cancellationToken = default);

    /// <summary>Adds steps (saved with the next <see cref="SaveChangesAsync"/>).</summary>
    void AddSteps(IEnumerable<AgentRunStep> steps);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class AgentRunRepository(AgenticDbContext context) : IAgentRunRepository
{
    public Task<AgentRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Runs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<List<AgentRunStep>> GetStepsAsync(Guid runId, CancellationToken cancellationToken = default) =>
        context.Steps.Where(s => s.RunId == runId).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);

    public Task<AgentRunStep?> GetStepAsync(Guid runId, Guid stepId, CancellationToken cancellationToken = default) =>
        context.Steps.FirstOrDefaultAsync(s => s.RunId == runId && s.Id == stepId, cancellationToken);

    public async Task AddAsync(AgentRun run, CancellationToken cancellationToken = default)
    {
        await context.Runs.AddAsync(run, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    public void AddSteps(IEnumerable<AgentRunStep> steps) => context.Steps.AddRange(steps);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
