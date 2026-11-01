using Hangfire.Common;
using Hangfire.States;

namespace Shared.Email.Jobs;

/// <summary>
/// Dead-letter alerting filter. Hangfire calls OnStateElection whenever it decides what
/// state to transition a job to. Because [AutomaticRetry] is a job-level filter (runs
/// before global filters), it has already changed FailedState → ScheduledState for any
/// retry that still has attempts left. By the time this filter runs, CandidateState is
/// only FailedState when ALL retries are exhausted — i.e. permanent failure only.
/// Registered via GlobalJobFilters.Filters.Add in UseEmailInfrastructure (after the
/// DI container is built) so it receives a proper ILogger from DI.
/// </summary>
public sealed class EmailFailedStateFilter(ILogger<EmailFailedStateFilter> logger) : IElectStateFilter
{
    public void OnStateElection(ElectStateContext context)
    {
        if (context.CandidateState is not FailedState failedState)
            return;

        var jobType = context.BackgroundJob.Job?.Type;
        if (jobType?.Assembly != EmailRoot.AssemblyName)
            return;

        EmailMessage? message = null;
        try { message = context.BackgroundJob.Job.Args.FirstOrDefault() as EmailMessage; }
        catch { /* args may not be deserializable — still log what we can */ }

        var retryCount = context.GetJobParameter<int>("RetryCount");

        logger.LogCritical(
            "Email permanently failed after {TotalAttempts} attempts. " +
            "TemplateName: {TemplateName} Recipients: {Recipients} JobId: {JobId} Error: {Error}",
            retryCount,
            message?.Template.ToString() ?? "unknown",
            message is null ? "unknown" : string.Join(", ", message.Recipients),
            context.BackgroundJob.Id,
            failedState.Exception?.Message ?? "unknown");
    }
}
