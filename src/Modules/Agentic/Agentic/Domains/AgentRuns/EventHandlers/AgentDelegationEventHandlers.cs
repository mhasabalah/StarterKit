using Agentic.Domains.AgentRuns.Events;
using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Runtime.Execution;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Domains.AgentRuns.EventHandlers;

/// <summary>
/// A delegated run finished (ADR-0033): its outcome becomes the result of the parent's <c>agent.delegate</c> call and
/// the parent is queued again. When the parent had not saved its own suspension yet, a check runs again shortly after
/// (<see cref="ResolveAgentDelegationJob"/>); the parent's next attempt also finds a finished delegated run itself.
/// </summary>
public sealed class ResumeParentOnDelegateFinishedEventHandler(
    AgenticDbContext dbContext,
    AgentDelegationResolver resolver,
    IBackgroundJobClient jobs,
    ILogger<ResumeParentOnDelegateFinishedEventHandler> logger) : INotificationHandler<AgentRunFinishedEvent>
{
    /// <summary>When the parent was still saving its suspension, look again after this long.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(30);

    public async Task Handle(AgentRunFinishedEvent notification, CancellationToken cancellationToken)
    {
        if (!await dbContext.Runs.AnyAsync(r => r.Id == notification.RunId && r.ParentRunId != null, cancellationToken))
            return;

        try
        {
            if (await resolver.ResolveAsync(notification.RunId, cancellationToken) == AgentDelegationResolver.Outcome.ParentNotYetWaiting)
                ResolveAgentDelegationJob.Schedule(jobs, notification.RunId, RecheckAfter);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A concurrent change of the parent (a cancel request): check again shortly instead of failing the child.
            logger.LogWarning(ex, "Agent run {RunId}: its parent was not resumed now; checking again", notification.RunId);
            ResolveAgentDelegationJob.Schedule(jobs, notification.RunId, RecheckAfter);
        }
    }
}

/// <summary>
/// A run that delegated finished (cancelled, failed): the runs it delegated to that are still going are cancelled too,
/// so nothing keeps working for a request nobody waits for.
/// </summary>
public sealed class CancelDelegatesOnRunFinishedEventHandler(
    AgenticDbContext dbContext,
    IUnitOfWorkRunner unitOfWork,
    TimeProvider timeProvider,
    ILogger<CancelDelegatesOnRunFinishedEventHandler> logger) : INotificationHandler<AgentRunFinishedEvent>
{
    private const int Attempts = 3;

    private static readonly AgentRunStatus[] FinishedStatuses =
        [AgentRunStatus.Completed, AgentRunStatus.Failed, AgentRunStatus.Cancelled];

    public async Task Handle(AgentRunFinishedEvent notification, CancellationToken cancellationToken)
    {
        var children = await dbContext.Runs.AsNoTracking()
            .Where(r => r.ParentRunId == notification.RunId && !FinishedStatuses.Contains(r.Status))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var childId in children)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    // Its own unit of work: a running delegate saves checkpoints concurrently, so a conflict reloads and
                    // asks again; the delegate's own loop also stops at its next checkpoint once its parent is finished.
                    await unitOfWork.RunAsync(async (services, ct) =>
                    {
                        var runs = services.GetRequiredService<IAgentRunRepository>();
                        if (await runs.GetByIdAsync(childId, ct) is { IsTerminal: false } child)
                        {
                            child.RequestCancel(timeProvider.GetUtcNow().UtcDateTime);
                            await runs.SaveChangesAsync(ct);
                        }

                        return true;
                    }, CancellationToken.None);
                    break;
                }
                catch (Exception ex) when (ex.IsConcurrencyConflict() && attempt < Attempts)
                {
                    logger.LogDebug(ex, "Delegated run {ChildId} changed while it was cancelled; trying again", childId);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Agent run {RunId}: the delegated run {ChildId} was not cancelled (it stops when it " +
                                          "sees its parent finished)", notification.RunId, childId);
                    break;
                }
            }
        }

        if (children.Count > 0)
            logger.LogInformation("Agent run {RunId} finished: {Count} delegated run(s) asked to stop", notification.RunId,
                children.Count);
    }
}
