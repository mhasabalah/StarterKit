using Agentic.Runtime.Execution;
using Hangfire;

namespace Agentic.Domains.AgentRuns.Jobs;

/// <summary>
/// Runs one attempt of an agent run on the <c>agentic</c> queue. Hangfire's automatic retry is off: the run row decides
/// whether and when there is another attempt (<see cref="AgentRunExecutor"/>). A duplicate delivery is harmless: only
/// the worker that wins the lease proceeds.
/// </summary>
[Queue(AgenticOptions.Queue)]
[AutomaticRetry(Attempts = 0)]
public sealed class ExecuteAgentRunJob(AgentRunExecutor executor)
{
    public Task ExecuteAsync(Guid runId, CancellationToken cancellationToken) => executor.ExecuteAsync(runId, cancellationToken);
}

/// <summary>Queues the Hangfire job of a run (the <c>agentic</c> queue comes from the job attributes).</summary>
public static class AgentRunQueue
{
    public static string Enqueue(IBackgroundJobClient jobs, Guid runId) =>
        jobs.Enqueue<ExecuteAgentRunJob>(job => job.ExecuteAsync(runId, CancellationToken.None));

    public static string Schedule(IBackgroundJobClient jobs, Guid runId, TimeSpan delay) =>
        jobs.Schedule<ExecuteAgentRunJob>(job => job.ExecuteAsync(runId, CancellationToken.None), delay);
}
