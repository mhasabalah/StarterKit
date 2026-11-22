using System.Text.Json;
using Agentic.Domains.AgentRuns.Jobs;
using Hangfire;

namespace Agentic.Runtime.Execution;

/// <summary>
/// The built-in tool <c>agent.delegate</c> (ADR-0033): its arguments, and the tool result a finished delegated run
/// becomes for the run that delegated.
/// </summary>
public static class AgentDelegation
{
    public sealed record Arguments(string AgentKey, string Task);

    /// <summary>Parses the model's (untrusted) arguments: exactly <c>agentKey</c> and a non-empty <c>task</c>.</summary>
    public static bool TryParse(string? json, int maxTaskLength, out Arguments? arguments, out string? error)
    {
        arguments = null;
        error = null;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                root.EnumerateObject().Any(p => p.Name is not ("agentKey" or "task")) ||
                !root.TryGetProperty("agentKey", out var key) || key.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("task", out var task) || task.ValueKind != JsonValueKind.String)
            {
                error = "Expected {\"agentKey\": string, \"task\": string}.";
                return false;
            }

            var text = task.GetString()!.Trim();
            if (text.Length == 0 || text.Length > maxTaskLength)
            {
                error = $"The task must have between 1 and {maxTaskLength} characters.";
                return false;
            }

            arguments = new Arguments(key.GetString()!, text);
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>What the delegating model is told: the delegated run, its status, and its answer or failure code.</summary>
    public static string Result(AgentRun child)
    {
        JsonElement? result = null;
        if (child.OutputJson is not null)
        {
            using var document = JsonDocument.Parse(child.OutputJson);
            result = document.RootElement.Clone();
        }

        return JsonSerializer.Serialize(new
        {
            runId = child.Id,
            agent = child.AgentKey,
            status = child.Status.ToString(),
            answer = child.OutputText,
            result,
            failureCode = child.FailureCode
        }, AgentJson.Output);
    }

    public static long DurationMs(AgentRun child) =>
        child is { StartedAt: { } started, FinishedAt: { } finished } ? (long)(finished - started).TotalMilliseconds : 0;
}

/// <summary>
/// Gives a finished delegated run's outcome back to the run that delegated, and puts that run back in the queue.
/// Called by <c>ResumeParentOnDelegateFinishedEventHandler</c> and, once more after a short delay, by
/// <see cref="ResolveAgentDelegationJob"/> (the parent may still have been saving its own suspension).
/// </summary>
public sealed class AgentDelegationResolver(
    AgenticDbContext dbContext,
    IAgentRunRepository runs,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    ILogger<AgentDelegationResolver> logger)
{
    public enum Outcome
    {
        /// <summary>The parent was waiting: it has the result and is queued.</summary>
        Resumed,

        /// <summary>Nothing to do: not a delegated run, not finished, or the parent no longer waits for it.</summary>
        Nothing,

        /// <summary>The parent has not saved its suspension yet; its own attempt (or a later check) handles it.</summary>
        ParentNotYetWaiting
    }

    public async Task<Outcome> ResolveAsync(Guid childRunId, CancellationToken cancellationToken)
    {
        var child = await dbContext.Runs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == childRunId, cancellationToken);
        if (child is not { IsTerminal: true, ParentRunId: { } parentId, ParentStepId: { } stepId })
            return Outcome.Nothing;

        var parent = await runs.GetByIdAsync(parentId, cancellationToken);
        var step = parent is null ? null : await runs.GetStepAsync(parentId, stepId, cancellationToken);
        if (parent is null || step is null || parent.IsTerminal)
            return Outcome.Nothing;
        if (parent.Status != AgentRunStatus.AwaitingDelegate)
            return step.IsOpen ? Outcome.ParentNotYetWaiting : Outcome.Nothing;

        if (!parent.CompleteDelegation(step, AgentDelegation.Result(child), child.Status == AgentRunStatus.Completed,
                child.Tainted, child.TotalTokens, AgentDelegation.DurationMs(child), timeProvider.GetUtcNow().UtcDateTime))
        {
            return Outcome.Nothing;
        }

        await runs.SaveChangesAsync(cancellationToken);
        AgentRunQueue.Enqueue(jobs, parent.Id);
        logger.LogInformation("Agent run {RunId} resumed: its delegated run {ChildId} finished ({Status})", parent.Id,
            child.Id, child.Status);
        return Outcome.Resumed;
    }
}
