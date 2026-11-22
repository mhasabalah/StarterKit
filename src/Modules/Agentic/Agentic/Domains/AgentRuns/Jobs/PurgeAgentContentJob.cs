using Agentic.Domains.AgentRuns.Services;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Agentic.Domains.AgentRuns.Jobs;

/// <summary>
/// Recurring (daily), per tenant: content retention. Erases the content of runs that finished more than
/// <c>Agentic:Content:RetentionDays</c> ago (0 = keep) and deletes expired memories. Batched and idempotent.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class PurgeAgentContentJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    ILogger<PurgeAgentContentJob> logger)
{
    internal const int BatchSize = 200;

    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await PurgeCurrentTenantAsync(cancellationToken),
            nameof(PurgeAgentContentJob));
        return total;
    }

    /// <summary>Purges the CURRENT tenant; returns runs erased plus memories deleted.</summary>
    public async Task<int> PurgeCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var total = await unitOfWork.RunAsync(
            (services, ct) => services.GetRequiredService<AgenticDbContext>().Memories
                .Where(m => m.ExpiresAt != null && m.ExpiresAt <= now)
                .ExecuteDeleteAsync(ct),
            CancellationToken.None);

        var retentionDays = options.Value.Content.RetentionDays;
        if (retentionDays > 0)
        {
            var cutoff = now.AddDays(-retentionDays);
            int erased;
            do
            {
                cancellationToken?.ThrowIfCancellationRequested();
                erased = await unitOfWork.RunAsync(async (services, ct) =>
                {
                    var db = services.GetRequiredService<AgenticDbContext>();
                    var ids = await db.Runs
                        .Where(r => r.FinishedAt != null && r.FinishedAt < cutoff && r.ContentErasedAt == null)
                        .OrderBy(r => r.FinishedAt)
                        .Select(r => r.Id)
                        .Take(BatchSize)
                        .ToListAsync(ct);
                    return await AgentRunContentEraser.EraseAsync(db, ids, now, ct);
                }, CancellationToken.None);
                total += erased;
            } while (erased >= BatchSize);

            // Conversation titles are user content too: dropped once the conversation is idle for as long.
            total += await unitOfWork.RunAsync((services, ct) => services.GetRequiredService<AgenticDbContext>().Conversations
                .Where(c => c.Title != null && c.LastMessageAt < cutoff)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.Title, (string?)null), ct), CancellationToken.None);
        }

        // Backstop for deleted conversations: a turn that finished after the deletion and was not erased then.
        int orphans;
        do
        {
            cancellationToken?.ThrowIfCancellationRequested();
            orphans = await unitOfWork.RunAsync(async (services, ct) =>
            {
                var db = services.GetRequiredService<AgenticDbContext>();
                var ids = await db.Runs
                    .Where(r => r.FinishedAt != null && r.ContentErasedAt == null &&
                                db.Conversations.Any(c => c.Id == r.ConversationId && c.Status == AgentConversationStatus.Deleted))
                    .OrderBy(r => r.FinishedAt)
                    .Select(r => r.Id)
                    .Take(BatchSize)
                    .ToListAsync(ct);
                return await AgentRunContentEraser.EraseAsync(db, ids, now, ct);
            }, CancellationToken.None);
            total += orphans;
        } while (orphans >= BatchSize);

        if (total > 0)
            logger.LogInformation("PurgeAgentContentJob: {Count} run(s) erased or memory(ies) deleted", total);
        return total;
    }
}
