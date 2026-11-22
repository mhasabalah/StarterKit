using System.Diagnostics;
using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Telemetry;
using Hangfire;
using Microsoft.Extensions.Options;
using Shared.Core.Entitlements;

namespace Agentic.Domains.AgentRuns.Services;

/// <summary>What a caller that already authorized the start hands to <see cref="AgentRunStarter"/>.</summary>
public sealed record AgentRunStart(
    RegisteredAgent Agent,
    AgentTriggerKind Trigger,
    Guid InitiatedBy,
    string? Prompt,
    string? InputJson,
    string Culture,
    IReadOnlyCollection<string>? PermissionCeiling = null,
    Guid? ApiKeyId = null,
    string? TriggerKey = null,
    Guid? ConversationId = null,
    int? ConversationTurn = null,
    bool StartTainted = false,
    Guid? ParentRunId = null,
    Guid? ParentStepId = null,
    int Depth = 0,
    AgentAutonomy? AutonomyCeiling = null,
    long? TokenCeiling = null);

/// <summary>
/// The one place a run is created, for every kind of trigger. The caller has decided WHO may start (endpoint policy,
/// agent permission, on-behalf-of user); this decides what the run is allowed to be:
/// <list type="bullet">
/// <item><b>Tenant policy</b> (<see cref="AgentPolicy"/>): the agent may be switched off, automatic triggers refused,
/// autonomy and budgets lowered, a person demanded for every change. Always the STRICTER of policy and definition.</item>
/// <item><b>Capacity</b>: runs of the tenant that are queued, running or waiting, against
/// <c>Agentic:Limits:MaxConcurrentRunsPerTenant</c> and the plan limit <c>max_concurrent_agent_runs</c>.</item>
/// <item><b>Plan quota</b> <c>agent_runs</c>: one unit per run, consumed just before the run is saved.</item>
/// </list>
/// What is fixed here and copied onto the run never changes afterwards (agent version, model, autonomy, budgets, API
/// key ceiling). What is deliberately NOT stored: the user's permissions, read again for every tool call.
/// </summary>
public sealed class AgentRunStarter(
    IAgentRunRepository repository,
    AgenticDbContext dbContext,
    IQuotaService quotas,
    IEntitlementService entitlements,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    AgenticMetrics metrics,
    ILogger<AgentRunStarter> logger)
{
    private static readonly AgentRunStatus[] ActiveStatuses =
        [AgentRunStatus.Queued, AgentRunStatus.Running, AgentRunStatus.AwaitingApproval, AgentRunStatus.AwaitingDelegate];

    public async Task<AgentRun> StartAsync(AgentRunStart start, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            throw new AgenticDisabledException();

        var definition = start.Agent.Definition;
        var policy = (await dbContext.Policies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.AgentKey == definition.Key, cancellationToken))?.Settings ?? AgentPolicySettings.Default;

        if (!policy.Enabled)
            throw new AgentAccessDeniedException("agent_disabled", definition.Key);
        if (!policy.AllowAutomaticTriggers && start.Trigger is AgentTriggerKind.Event or AgentTriggerKind.Schedule)
            throw new AgentAccessDeniedException("automatic_triggers_disabled", definition.Key);

        await EnsureCapacityAsync(settings, cancellationToken);

        var autonomy = policy.AutonomyCeiling is { } ceiling && ceiling < definition.Autonomy ? ceiling : definition.Autonomy;
        // A delegated run never acts with more autonomy than the run that delegated (ADR-0033).
        if (start.AutonomyCeiling is { } parent && parent < autonomy)
            autonomy = parent;
        var budget = Budget(settings.Limits, definition.Limits, policy);
        if (start.TokenCeiling is { } tokens)
            budget = budget with { MaxTokens = Math.Max(1, Math.Min(budget.MaxTokens, tokens)) };
        var run = AgentRun.Start(
            definition.Key,
            definition.Version,
            definition.Model ?? settings.DefaultModel,
            autonomy,
            start.Trigger,
            start.InitiatedBy,
            start.Prompt?.Trim(),
            start.InputJson,
            budget,
            start.Culture,
            start.PermissionCeiling,
            start.TriggerKey,
            start.ConversationId,
            Activity.Current?.Id,
            start.ApiKeyId,
            policy.RequireApprovalForChanges,
            start.StartTainted,
            start.ConversationTurn,
            start.ParentRunId,
            start.ParentStepId,
            start.Depth);
        run.MarkEnqueued(timeProvider.GetUtcNow().UtcDateTime);

        // Consumed last, once everything that can refuse the run without touching the database has passed (the
        // aggregate's own validation included). The quota service has no refund, so a save that fails after this point
        // (a duplicate trigger that lost the race, a database error) still costs one unit.
        var quota = await quotas.TryConsumeAsync(EntitlementKeys.AgentRuns, 1, cancellationToken);
        if (!quota.Allowed)
            throw new QuotaExceededException(EntitlementKeys.AgentRuns, quota.Used, quota.Limit);

        await repository.AddAsync(run, cancellationToken);
        AgentRunQueue.Enqueue(jobs, run.Id);

        metrics.RecordStarted(run);
        logger.LogInformation("Agent run {RunId} of {Agent} v{Version} queued ({Trigger}, autonomy {Autonomy})", run.Id,
            run.AgentKey, run.AgentVersion, run.TriggerKind, run.Autonomy);
        return run;
    }

    private async Task EnsureCapacityAsync(AgenticOptions settings, CancellationToken cancellationToken)
    {
        var limits = new List<int>(2);
        if (settings.Limits.MaxConcurrentRunsPerTenant > 0)
            limits.Add(settings.Limits.MaxConcurrentRunsPerTenant);
        if (await entitlements.GetLimitAsync(EntitlementKeys.MaxConcurrentAgentRuns, cancellationToken) is { } plan and >= 0)
            limits.Add(plan);
        if (limits.Count == 0)
            return;

        // A check, not a lock: two starts at the same instant may both pass and overshoot by one. That is acceptable
        // for a capacity guard (the per-run budgets and the plan quota are the hard bounds).
        var limit = limits.Min();
        var active = await dbContext.Runs.CountAsync(r => ActiveStatuses.Contains(r.Status), cancellationToken);
        if (active >= limit)
            throw new AgentRunLimitException("too_many_concurrent_runs", limit);
    }

    /// <summary>The module's ceilings, lowered (never raised) by what the agent and the tenant's policy ask for.</summary>
    internal static AgentRunBudget Budget(AgentRunLimitOptions limits, AgentLimits? agent, AgentPolicySettings? policy = null) => new(
        Lower(Lower(limits.MaxModelTurns, agent?.MaxModelTurns), policy?.MaxModelTurns),
        LowerOrZero(Lower(limits.MaxToolCalls, agent?.MaxToolCalls), policy?.MaxToolCalls),
        Lower(Lower(limits.MaxTokens, agent?.MaxTokens), policy?.MaxTokens),
        Lower(limits.MaxDurationSeconds, agent?.MaxDurationSeconds));

    private static int Lower(int ceiling, int? requested) =>
        requested is > 0 and var value ? Math.Min(ceiling, value) : ceiling;

    /// <summary>A tenant may set tool calls to 0 (the agent may only talk).</summary>
    private static int LowerOrZero(int ceiling, int? requested) =>
        requested is >= 0 and var value ? Math.Min(ceiling, value) : ceiling;

    private static long Lower(long ceiling, long? requested) =>
        requested is > 0 and var value ? Math.Min(ceiling, value) : ceiling;
}
