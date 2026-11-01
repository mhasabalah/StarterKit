using Hangfire.States;
using Hangfire.Common;

namespace Shared.Notification.Jobs;

public sealed class NotificationFailedStateFilter(
    ILogger<NotificationFailedStateFilter> logger) : IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not FailedState failedState)
            return;

        var jobType = context.BackgroundJob.Job?.Type;
        if (jobType?.Assembly != NotificationRoot.AssemblyName)
            return;

        NotificationMessage? message = null;
        try { message = context.BackgroundJob.Job.Args.FirstOrDefault() as NotificationMessage; }
        catch { }

        logger.LogCritical(
            "Notification job permanently failed. TemplateName: {TemplateName} UserId: {UserId} JobId: {JobId} Error: {Error}",
            message?.TemplateName ?? "unknown",
            message?.UserId.ToString() ?? "unknown",
            context.BackgroundJob.Id,
            failedState.Exception?.Message ?? "unknown");
    }
}
