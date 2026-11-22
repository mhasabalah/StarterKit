using Agentic.Domains.AgentRuns.Events;
using Agentic.Domains.AgentRuns.Services;

namespace Agentic.Domains.Conversations.EventHandlers;

/// <summary>
/// A turn that was still going when its conversation was deleted keeps its content until it finishes (a run needs its
/// transcript to continue or to stop cleanly). When it does finish, its content is erased here. Should this fail, the
/// daily <c>PurgeAgentContentJob</c> erases the finished turns of deleted conversations.
/// </summary>
public sealed class EraseDeletedConversationTurnEventHandler(
    AgenticDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<EraseDeletedConversationTurnEventHandler> logger) : INotificationHandler<AgentRunFinishedEvent>
{
    public async Task Handle(AgentRunFinishedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await dbContext.Runs.AnyAsync(r =>
                r.Id == notification.RunId && r.ContentErasedAt == null &&
                dbContext.Conversations.Any(c => c.Id == r.ConversationId && c.Status == AgentConversationStatus.Deleted),
                cancellationToken);
            if (deleted)
                await AgentRunContentEraser.EraseAsync(dbContext, [notification.RunId], timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Agent run {RunId}: the turn of a deleted conversation was not erased (the daily purge will)",
                notification.RunId);
        }
    }
}
