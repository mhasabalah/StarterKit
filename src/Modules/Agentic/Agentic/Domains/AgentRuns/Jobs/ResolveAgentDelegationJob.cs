using Agentic.Runtime.Execution;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Domains.AgentRuns.Jobs;

/// <summary>
/// Looks once more whether a finished delegated run's parent can be resumed (ADR-0033): the case where the parent was
/// still saving its suspension when the delegated run finished. Idempotent: a parent that was already resumed, or no
/// longer waits, is left alone.
/// </summary>
[Queue(AgenticOptions.Queue)]
[AutomaticRetry(Attempts = 3)]
public sealed class ResolveAgentDelegationJob(IUnitOfWorkRunner unitOfWork)
{
    public Task ExecuteAsync(Guid childRunId, CancellationToken cancellationToken) =>
        unitOfWork.RunAsync((services, ct) => services.GetRequiredService<AgentDelegationResolver>().ResolveAsync(childRunId, ct),
            cancellationToken);

    public static string Schedule(IBackgroundJobClient jobs, Guid childRunId, TimeSpan delay) =>
        jobs.Schedule<ResolveAgentDelegationJob>(job => job.ExecuteAsync(childRunId, CancellationToken.None), delay);
}
