using Agentic.Domains.AgentRuns.Events;
using Agentic.Domains.Conversations.Jobs;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Agentic.Domains.Conversations.EventHandlers;

/// <summary>
/// After a turn finishes, queues <see cref="SummarizeAgentConversationJob"/> when more than
/// <see cref="AgentConversationOptions.SummarizeAfterTurns"/> finished turns are not covered by the conversation's
/// summary (ADR-0031). Best effort: a failure here only delays the summary to the next turn.
/// </summary>
public sealed class ScheduleConversationSummaryEventHandler(
    AgenticDbContext dbContext,
    IBackgroundJobClient jobs,
    IOptions<AgenticOptions> options,
    ILogger<ScheduleConversationSummaryEventHandler> logger) : INotificationHandler<AgentRunFinishedEvent>
{
    private static readonly AgentRunStatus[] FinishedStatuses =
        [AgentRunStatus.Completed, AgentRunStatus.Failed, AgentRunStatus.Cancelled];

    public async Task Handle(AgentRunFinishedEvent notification, CancellationToken cancellationToken)
    {
        var limits = options.Value.Conversations;
        if (!options.Value.Enabled || limits.SummarizeAfterTurns == 0)
            return;

        try
        {
            var conversation = await dbContext.Runs.AsNoTracking()
                .Where(r => r.Id == notification.RunId && r.ConversationId != null)
                .Join(dbContext.Conversations.Where(c => c.Status == AgentConversationStatus.Active),
                    r => r.ConversationId, c => c.Id,
                    (r, c) => new { c.Id, After = c.Summary == null ? 0 : c.SummaryThroughTurn })
                .FirstOrDefaultAsync(cancellationToken);
            if (conversation is null)
                return;

            var uncovered = await dbContext.Runs.CountAsync(r =>
                r.ConversationId == conversation.Id && r.ConversationTurn > conversation.After &&
                FinishedStatuses.Contains(r.Status) && r.ContentErasedAt == null, cancellationToken);
            if (uncovered > limits.SummarizeAfterTurns)
                ConversationSummaryQueue.Enqueue(jobs, conversation.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Agent run {RunId}: the conversation summary was not scheduled", notification.RunId);
        }
    }
}
