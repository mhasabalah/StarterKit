using Agentic.Contracts.Approvals;
using Agentic.Telemetry;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Agentic.Domains.Approvals.Jobs;

/// <summary>
/// Recurring, per tenant: a request nobody decided before <see cref="AgentApproval.ExpiresAt"/> expires, and the run
/// that waits for it fails with <c>approval_expired</c>. A suspended run therefore never waits forever, and an old
/// request can never be approved later under conditions nobody reviewed. One unit of work per request.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class ExpireAgentApprovalsJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    AgenticMetrics metrics,
    ILogger<ExpireAgentApprovalsJob> logger)
{
    internal const int BatchSize = 100;

    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        if (!options.Value.Enabled)
            return 0;

        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await ExpireCurrentTenantAsync(cancellationToken),
            nameof(ExpireAgentApprovalsJob));
        return total;
    }

    public async Task<int> ExpireCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var due = await unitOfWork.RunAsync(async (services, ct) =>
            await services.GetRequiredService<AgenticDbContext>().Approvals.AsNoTracking()
                .Where(a => a.Status == AgentApprovalStatus.Pending && a.ExpiresAt <= now)
                .OrderBy(a => a.ExpiresAt)
                .Select(a => a.Id)
                .Take(BatchSize)
                .ToListAsync(ct), CancellationToken.None);

        var expired = 0;
        foreach (var id in due)
        {
            cancellationToken?.ThrowIfCancellationRequested();
            try
            {
                if (await ExpireOneAsync(id, now))
                    expired++;
            }
            catch (Exception ex) when (ex.IsConcurrencyConflict())
            {
                // Decided (or cancelled) at the same moment: that decision stands.
                logger.LogInformation("Approval {ApprovalId} changed while it was being expired; left as it is", id);
            }
        }

        return expired;
    }

    private Task<bool> ExpireOneAsync(Guid approvalId, DateTime now) =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            var approvals = services.GetRequiredService<IAgentApprovalRepository>();
            var approval = await approvals.GetByIdAsync(approvalId, ct);
            if (approval is null || !approval.IsExpired(now))
                return false;

            approval.Expire();
            var run = await services.GetRequiredService<IAgentRunRepository>().GetByIdAsync(approval.RunId, ct);
            var failed = run is { Status: AgentRunStatus.AwaitingApproval };
            if (failed)
            {
                using var culture = AgenticText.UseCulture(run!.Culture);
                run.Fail("approval_expired", AgenticText.Get("Agent.Failure.approval_expired", approval.ToolName), now);
            }

            await approvals.SaveChangesAsync(ct);
            if (failed)
                metrics.RecordFinished(run!);
            logger.LogInformation("Approval {ApprovalId} of agent run {RunId} expired", approval.Id, approval.RunId);
            return true;
        }, CancellationToken.None);
}
