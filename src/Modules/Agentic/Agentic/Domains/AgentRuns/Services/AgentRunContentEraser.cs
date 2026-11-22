namespace Agentic.Domains.AgentRuns.Services;

/// <summary>
/// Removes what people and models WROTE in finished runs (prompt, input, outputs, transcript text, tool arguments and
/// results, approval payloads and rationale, and the summary of their conversation) and keeps the facts (who, when, which agent and tools, status, counters,
/// costs, audit entries). Set-based and idempotent; it never touches a run that is still going, because a run needs its
/// transcript to continue.
/// </summary>
public static class AgentRunContentEraser
{
    /// <summary>Erases the content of these FINISHED runs of the current tenant; returns the runs erased.</summary>
    public static async Task<int> EraseAsync(AgenticDbContext db, IReadOnlyCollection<Guid> runIds, DateTime now,
        CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
            return 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Read ONCE: a run that finishes between the statements below must be erased by all of them or by none
        // (finished is final, so this list stays true for the whole transaction).
        var finished = await db.Runs.Where(r => runIds.Contains(r.Id) &&
                                                (r.Status == AgentRunStatus.Completed || r.Status == AgentRunStatus.Failed ||
                                                 r.Status == AgentRunStatus.Cancelled))
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);
        if (finished.Count == 0)
            return 0;

        await db.Steps.Where(s => finished.Contains(s.RunId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Text, (string?)null)
                .SetProperty(x => x.ArgumentsJson, (string?)null)
                .SetProperty(x => x.ResultJson, (string?)null), cancellationToken);
        await db.Approvals.Where(a => finished.Contains(a.RunId))
            .ExecuteUpdateAsync(a => a
                .SetProperty(x => x.ArgumentsJson, "{}")
                .SetProperty(x => x.ModelRationale, (string?)null)
                .SetProperty(x => x.DecisionNote, (string?)null), cancellationToken);
        var erased = await db.Runs.Where(r => finished.Contains(r.Id))
            .ExecuteUpdateAsync(r => r
                .SetProperty(x => x.Prompt, (string?)null)
                .SetProperty(x => x.InputJson, (string?)null)
                .SetProperty(x => x.OutputJson, (string?)null)
                .SetProperty(x => x.OutputText, (string?)null)
                .SetProperty(x => x.ContentErasedAt, now), cancellationToken);
        // A conversation summary was written from turns' content (ADR-0031): it goes with any of them. The next summary
        // is written again from the turns whose content still exists.
        await db.Conversations.Where(c => c.Summary != null && db.Runs.Any(r => finished.Contains(r.Id) && r.ConversationId == c.Id))
            .ExecuteUpdateAsync(c => c
                .SetProperty(x => x.Summary, (string?)null)
                .SetProperty(x => x.SummaryThroughTurn, 0)
                .SetProperty(x => x.SummaryUpdatedAt, (DateTime?)null), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return erased;
    }
}
