using Agentic.Domains.AgentRuns.Events;

namespace Agentic.Domains.Approvals.EventHandlers;

/// <summary>
/// When a run finishes, every approval request of it that is still open (pending, or approved but never used) is
/// closed. However the run ended (cancelled by a user, failed on a budget or on lost access, expired), nothing is left
/// for an approver to decide and no approval stays valid for a run that will never use it. Dispatched after the
/// finishing save committed; a decision made in between is refused because the run is no longer waiting.
/// </summary>
public sealed class CloseApprovalsOnRunFinishedEventHandler(
    IAgentApprovalRepository approvals,
    ILogger<CloseApprovalsOnRunFinishedEventHandler> logger) : INotificationHandler<AgentRunFinishedEvent>
{
    public async Task Handle(AgentRunFinishedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var open = await approvals.GetOpenForRunAsync(notification.RunId, cancellationToken);
            if (open.Count == 0)
                return;

            foreach (var approval in open)
                approval.Cancel();
            await approvals.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            // Decided or expired at the same moment: that outcome stands, and the run is finished either way.
            logger.LogInformation("Agent run {RunId}: an approval changed while it was being closed", notification.RunId);
        }
    }
}
