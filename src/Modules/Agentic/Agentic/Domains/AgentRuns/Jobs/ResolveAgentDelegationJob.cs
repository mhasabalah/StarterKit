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
    public async Task ExecuteAsync(Guid childRunId, CancellationToken cancellationToken)
    {
        var outcome = await unitOfWork.RunAsync((services, ct) =>
            services.GetRequiredService<AgentDelegationResolver>().ResolveAsync(childRunId, ct), cancellationToken);
        // Still saving its suspension: Hangfire tries again with backoff; RequeueStalledAgentRunsJob is the backstop.
        if (outcome == AgentDelegationResolver.Outcome.ParentNotYetWaiting)
            throw new InvalidOperationException($"The parent of agent run {childRunId} does not wait for it yet.");
    }

    public static string Schedule(IBackgroundJobClient jobs, Guid childRunId, TimeSpan delay) =>
        jobs.Schedule<ResolveAgentDelegationJob>(job => job.ExecuteAsync(childRunId, CancellationToken.None), delay);
}
