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

    /// <summary>The step this unit of work loaded or added last (what a progress event is about), or null.</summary>
    AgentRunStep? LastTouchedStep { get; }

    /// <summary>The approval request of a tool-call step, if one was made (tracked).</summary>
    Task<AgentApproval?> FindApprovalAsync(Guid stepId, CancellationToken cancellationToken = default);

    /// <summary>Adds an approval request (saved with the next <see cref="SaveChangesAsync"/>, in the run's transaction).</summary>
    void AddApproval(AgentApproval approval);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class AgentRunRepository(AgenticDbContext context) : IAgentRunRepository
{
    public Task<AgentRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Runs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<List<AgentRunStep>> GetStepsAsync(Guid runId, CancellationToken cancellationToken = default) =>
        context.Steps.Where(s => s.RunId == runId).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);

    public AgentRunStep? LastTouchedStep { get; private set; }

    public async Task<AgentRunStep?> GetStepAsync(Guid runId, Guid stepId, CancellationToken cancellationToken = default)
    {
        var step = await context.Steps.FirstOrDefaultAsync(s => s.RunId == runId && s.Id == stepId, cancellationToken);
        LastTouchedStep = step ?? LastTouchedStep;
        return step;
    }

    public async Task AddAsync(AgentRun run, CancellationToken cancellationToken = default)
    {
        await context.Runs.AddAsync(run, cancellationToken);
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // A run that could not be inserted (a duplicate trigger lost the race) must not stay in the change
            // tracker: the next save of this scope would try to insert it again.
            context.Entry(run).State = EntityState.Detached;
            throw;
        }
    }

    public void AddSteps(IEnumerable<AgentRunStep> steps)
    {
        foreach (var step in steps)
        {
            context.Steps.Add(step);
            LastTouchedStep = step;
        }
    }

    public Task<AgentApproval?> FindApprovalAsync(Guid stepId, CancellationToken cancellationToken = default) =>
        context.Approvals.FirstOrDefaultAsync(a => a.StepId == stepId, cancellationToken);

    public void AddApproval(AgentApproval approval) => context.Approvals.Add(approval);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);
}
