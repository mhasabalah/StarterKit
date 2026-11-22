using Agentic.Contracts.Memory;
using Agentic.Domains.AgentRuns.Services;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.MultiTenancy.BackgroundJobs;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Agentic.Domains.AgentRuns.Consumers;

/// <summary>
/// Account erasure, in EVERY tenant: the user's personal memories are deleted, their unfinished runs are cancelled,
/// the notes they wrote on approval decisions are removed, and the content of their finished runs is erased
/// (<see cref="AgentRunContentEraser"/>). Tenant memories the user
/// wrote and the runs' facts (counters, statuses, audit entries) are business records of the tenant and stay; the user
/// row is anonymized, so their ids are pseudonymous.
/// <para>
/// Idempotent. A run a worker is executing stops at its next checkpoint; until every run of the user has finished
/// (and in any tenant that failed or is migrating) the consumer throws after visiting all tenants, so the bus retries
/// and the finished part is a no-op then.
/// </para>
/// </summary>
public sealed class UserDeletedAgenticConsumer(
    ITenantJobRunner tenantRunner,
    ITenantStore tenantStore,
    IUnitOfWorkRunner unitOfWork,
    TimeProvider timeProvider,
    ILogger<UserDeletedAgenticConsumer> logger) : IConsumer<UserDeletedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<UserDeletedIntegrationEvent> context)
    {
        var userId = context.Message.UserId;
        var stillRunning = 0;

        var failedTenants = await tenantRunner.ForEachTenantScopeAsync(async (tenant, ct) =>
        {
            stillRunning += await EraseCurrentTenantAsync(userId, ct);
        }, nameof(UserDeletedAgenticConsumer), context.CancellationToken);

        var migrating = await tenantStore.GetMigratingTenantsAsync(context.CancellationToken);
        if (failedTenants > 0 || migrating.Count > 0 || stillRunning > 0)
        {
            throw new InvalidOperationException(
                $"Erasure of user {userId} in Agentic is incomplete ({failedTenants} failed, {migrating.Count} migrating " +
                $"tenant(s), {stillRunning} run(s) still stopping); will retry.");
        }
    }

    /// <summary>Erases the user's agent data in the CURRENT tenant; returns the runs that have not finished yet.</summary>
    public async Task<int> EraseCurrentTenantAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 1. Personal memories.
        var memories = await unitOfWork.RunAsync((services, ct) => services.GetRequiredService<AgenticDbContext>().Memories
            .Where(m => m.Scope == AgentMemoryScope.User && m.OwnerUserId == userId)
            .ExecuteDeleteAsync(ct), cancellationToken);

        // 2. Unfinished runs: cancelled through the aggregate (open approvals are closed by the finished-event handler).
        var running = await unitOfWork.RunAsync(async (services, ct) =>
        {
            var repository = services.GetRequiredService<IAgentRunRepository>();
            var runs = await services.GetRequiredService<AgenticDbContext>().Runs
                .Where(r => r.InitiatedBy == userId && r.Status != AgentRunStatus.Completed &&
                            r.Status != AgentRunStatus.Failed && r.Status != AgentRunStatus.Cancelled)
                .ToListAsync(ct);
            foreach (var run in runs)
            {
                if (!run.CancelRequested)
                    run.RequestCancel(now);
                // A cancel nobody will confirm: no worker holds the run (its lease expired or it was never claimed).
                if (!run.IsTerminal && (run.LeaseUntil is null || run.LeaseUntil < now))
                    run.ConfirmCancelled(now);
            }

            if (runs.Count > 0)
                await repository.SaveChangesAsync(ct);
            return runs.Count(r => !r.IsTerminal);
        }, cancellationToken);

        // 3. Notes the user wrote when deciding approvals (on anyone's runs).
        await unitOfWork.RunAsync((services, ct) => services.GetRequiredService<AgenticDbContext>().Approvals
            .Where(a => a.DecidedBy == userId && a.DecisionNote != null)
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.DecisionNote, (string?)null), ct), cancellationToken);

        // 4. Content of the finished runs.
        var erased = await unitOfWork.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<AgenticDbContext>();
            var ids = await db.Runs.Where(r => r.InitiatedBy == userId && r.ContentErasedAt == null)
                .Select(r => r.Id).ToListAsync(ct);
            return await AgentRunContentEraser.EraseAsync(db, ids, now, ct);
        }, cancellationToken);

        // 5. Their conversations: closed for good, titles and summaries dropped (the turns were erased above as their runs).
        await unitOfWork.RunAsync((services, ct) => services.GetRequiredService<AgenticDbContext>().Conversations
            .Where(c => c.OwnerId == userId && c.Status != AgentConversationStatus.Deleted)
            .ExecuteUpdateAsync(c => c
                .SetProperty(x => x.Status, AgentConversationStatus.Deleted)
                .SetProperty(x => x.Title, (string?)null)
                .SetProperty(x => x.Summary, (string?)null)
                .SetProperty(x => x.SummaryThroughTurn, 0)
                .SetProperty(x => x.SummaryUpdatedAt, (DateTime?)null)
                .SetProperty(x => x.DeletedAt, now), ct), cancellationToken);

        if (memories + erased > 0 || running > 0)
        {
            logger.LogInformation(
                "Erasure of user {UserId}: {Memories} memory(ies) deleted, {Erased} run(s) erased, {Running} still stopping",
                userId, memories, erased, running);
        }

        return running;
    }
}
