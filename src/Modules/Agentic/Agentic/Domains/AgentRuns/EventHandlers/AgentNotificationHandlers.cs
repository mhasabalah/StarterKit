using Agentic.Domains.AgentRuns.Events;
using Agentic.Domains.Approvals.Events;
using Agentic.Runtime.Execution;
using Notification.Contracts;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;

namespace Agentic.Domains.AgentRuns.EventHandlers;

/// <summary>Categories of the in-app notifications of the Agentic module (group <c>agents</c>).</summary>
public static class AgentNotificationCategories
{
    public const string ApprovalRequired = "agents.approval-required";
    public const string RunFinished = "agents.run-finished";
}

/// <summary>
/// Tells the user a run acts for that it needs a decision: an in-app inbox entry (pushed in real time over the
/// notification hub), dispatched after the suspending save committed. The entry names the action only; what exactly is
/// proposed is read from the approval request, by someone allowed to see it. Best effort: a notification problem never
/// undoes the suspension; the request is always listed under <c>GET /agent-approvals</c>.
/// </summary>
public sealed class AgentApprovalRequestedEventHandler(
    INotificationSender notifications,
    ILogger<AgentApprovalRequestedEventHandler> logger) : INotificationHandler<AgentApprovalRequestedEvent>
{
    public async Task Handle(AgentApprovalRequestedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // The correlation id is stable per request: a replayed dispatch does not add a second inbox entry.
            await notifications
                .For(notification.RequestedBy, Culture(notification.Culture))
                .WithInApp(Content(notification))
                .WithPush()
                .WithCorrelationId(notification.ApprovalId)
                .SendAsync(ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not notify user {UserId} that approval {ApprovalId} is waiting",
                notification.RequestedBy, notification.ApprovalId);
        }
    }

    public static InAppContent Content(AgentApprovalRequestedEvent e) => new(
        Title: new LocalizedText("Approval needed", "مطلوب موافقة"),
        Body: new LocalizedText(
            $"An agent is waiting for a decision on the action {e.ToolName}.",
            $"ينتظر أحد الوكلاء قراراً بشأن الإجراء {e.ToolName}."),
        Category: AgentNotificationCategories.ApprovalRequired,
        ActionUrl: $"/agent-approvals/{e.ApprovalId}",
        Severity: NotificationSeverity.Warning,
        Data: new Dictionary<string, string>
        {
            ["approvalId"] = e.ApprovalId.ToString(),
            ["runId"] = e.RunId.ToString(),
            ["agentKey"] = e.AgentKey,
            ["tool"] = e.ToolName
        });

    internal static NotificationCulture Culture(string culture) =>
        AgenticText.NormalizeCulture(culture) == "ar" ? NotificationCulture.Ar : NotificationCulture.En;
}

/// <summary>
/// Tells the initiator that their run finished (completed, failed or cancelled): the final state to the open sessions,
/// and an in-app inbox entry. Dispatched after the finishing save committed. Best effort.
/// </summary>
public sealed class AgentRunFinishedEventHandler(
    INotificationSender notifications,
    AgenticDbContext dbContext,
    AgentRunProgressPublisher progress,
    ILogger<AgentRunFinishedEventHandler> logger) : INotificationHandler<AgentRunFinishedEvent>
{
    public async Task Handle(AgentRunFinishedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            // Live: the final state (the progress stream ends here).
            var run = await dbContext.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == notification.RunId, cancellationToken);
            if (run is not null)
                await progress.PublishAsync(run);

            await notifications
                .For(notification.InitiatedBy, AgentApprovalRequestedEventHandler.Culture(notification.Culture))
                .WithInApp(Content(notification))
                .WithPush()
                .WithCorrelationId(notification.RunId)
                .SendAsync(ct: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not notify user {UserId} that agent run {RunId} finished",
                notification.InitiatedBy, notification.RunId);
        }
    }

    public static InAppContent Content(AgentRunFinishedEvent e)
    {
        var (en, ar, severity) = e.Status switch
        {
            AgentRunStatus.Completed => ("Agent run completed", "اكتمل تشغيل الوكيل", NotificationSeverity.Success),
            AgentRunStatus.Cancelled => ("Agent run cancelled", "تم إلغاء تشغيل الوكيل", NotificationSeverity.Info),
            _ => ("Agent run failed", "فشل تشغيل الوكيل", NotificationSeverity.Critical)
        };

        var data = new Dictionary<string, string>
        {
            ["runId"] = e.RunId.ToString(),
            ["agentKey"] = e.AgentKey,
            ["status"] = e.Status.ToString()
        };
        if (e.FailureCode is not null)
            data["failureCode"] = e.FailureCode;

        return new InAppContent(
            Title: new LocalizedText(en, ar),
            Body: new LocalizedText($"{en}: {e.AgentKey}.", $"{ar}: {e.AgentKey}."),
            Category: AgentNotificationCategories.RunFinished,
            ActionUrl: $"/agent-runs/{e.RunId}",
            Severity: severity,
            Data: data);
    }
}
