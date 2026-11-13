using System.Diagnostics;
using System.Text;
using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Domains.AgentRuns.Services;
using Agentic.Telemetry;
using Hangfire;
using Microsoft.Extensions.Options;

namespace Agentic.Domains.AgentRuns.Features;

public class StartAgentRunCommandValidator : AbstractValidator<StartAgentRunCommand>
{
    public StartAgentRunCommandValidator(IStringLocalizer<AgenticResource> localizer, IOptions<AgenticOptions> options)
    {
        var limits = options.Value.Limits;

        RuleFor(x => x.AgentKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.AgentKey.Required"])
            .MaximumLength(AgentRun.AgentKeyMaxLength).WithMessage(_ => localizer["Validation.AgentKey.MaxLength"]);
        RuleFor(x => x.Prompt)
            .MaximumLength(limits.MaxPromptLength).WithMessage(_ => localizer["Validation.Prompt.MaxLength", limits.MaxPromptLength]);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Prompt) || !string.IsNullOrWhiteSpace(x.InputJson))
            .WithName(nameof(StartAgentRunCommand.Prompt))
            .WithMessage(_ => localizer["Validation.Input.Required"]);
        RuleFor(x => x.InputJson)
            .Must(json => Encoding.UTF8.GetByteCount(json!) <= limits.MaxInputBytes)
            .WithMessage(_ => localizer["Validation.Input.TooLarge", limits.MaxInputBytes])
            .Must(AgentJson.IsJsonObject)
            .WithMessage(_ => localizer["Validation.Input.NotJsonObject"])
            .When(x => !string.IsNullOrWhiteSpace(x.InputJson));
    }
}

/// <summary>
/// Creates the run for the current user and queues it on <c>agentic</c>. Nothing runs inside the request. What is
/// fixed here and never changed later: the agent version, the model, the autonomy ceiling, the budgets and (for an API
/// key) the permission ceiling. What is deliberately NOT stored: the user's permissions, which are read again for
/// every tool call.
/// </summary>
public class StartAgentRunHandler(
    IAgentRunRepository repository,
    AgentRegistry registry,
    AgentAccess access,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    AgenticMetrics metrics,
    ILogger<StartAgentRunHandler> logger) : ICommandHandler<StartAgentRunCommand, AgentRunDto>
{
    public async Task<AgentRunDto> Handle(StartAgentRunCommand command, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
            throw new AgenticDisabledException();

        var agent = registry.GetAgent(command.AgentKey);
        var definition = agent.Definition;

        // An agent the caller may not start does not exist for them (no probing of agent keys).
        if (!await access.CanStartAsync(definition))
            throw new AgentNotFoundException(command.AgentKey);

        var trigger = access.IsApiKey ? AgentTriggerKind.Api : AgentTriggerKind.User;
        if (!definition.AllowedTriggers.Contains(trigger))
            throw new AgentAccessDeniedException("trigger_not_allowed", definition.Key, trigger);

        var run = AgentRun.Start(
            definition.Key,
            definition.Version,
            definition.Model ?? settings.DefaultModel,
            definition.Autonomy,
            trigger,
            access.UserId,
            command.Prompt?.Trim(),
            command.InputJson,
            Budget(settings.Limits, definition.Limits),
            AgenticText.CurrentCulture,
            access.PermissionCeiling,
            sessionId: command.SessionId,
            traceParent: Activity.Current?.Id,
            apiKeyId: access.ApiKeyId);
        run.MarkEnqueued(timeProvider.GetUtcNow().UtcDateTime);

        await repository.AddAsync(run, cancellationToken);
        AgentRunQueue.Enqueue(jobs, run.Id);

        metrics.RecordStarted(run);
        logger.LogInformation("Agent run {RunId} of {Agent} v{Version} queued ({Trigger})", run.Id, run.AgentKey,
            run.AgentVersion, run.TriggerKind);
        return AgentRunProjections.Map(run);
    }

    /// <summary>The module's ceilings, lowered (never raised) by what the agent asks for.</summary>
    internal static AgentRunBudget Budget(AgentRunLimitOptions limits, AgentLimits? agent) => new(
        Lower(limits.MaxModelTurns, agent?.MaxModelTurns),
        Lower(limits.MaxToolCalls, agent?.MaxToolCalls),
        Math.Min(limits.MaxTokens, agent?.MaxTokens is > 0 and var tokens ? tokens : limits.MaxTokens),
        Lower(limits.MaxDurationSeconds, agent?.MaxDurationSeconds));

    private static int Lower(int ceiling, int? requested) =>
        requested is > 0 and var value ? Math.Min(ceiling, value) : ceiling;
}

// ----- cancel ---------------------------------------------------------------------------------------------------

public class CancelAgentRunCommandValidator : AbstractValidator<CancelAgentRunCommand>
{
    public CancelAgentRunCommandValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.RunId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

/// <summary>
/// Cancels a run: at once when it is waiting, at its next checkpoint when a worker is running it (a tool that already
/// started is not interrupted, and what it committed stays committed). Only the initiator or a holder of
/// <c>agentic.manage</c>; for anyone else the run does not exist.
/// </summary>
public class CancelAgentRunHandler(
    IAgentRunRepository repository,
    AgenticDbContext dbContext,
    AgentAccess access,
    TimeProvider timeProvider,
    AgenticMetrics metrics,
    ILogger<CancelAgentRunHandler> logger) : ICommandHandler<CancelAgentRunCommand, AgentRunDto>
{
    private const int Attempts = 3;

    public async Task<AgentRunDto> Handle(CancelAgentRunCommand command, CancellationToken cancellationToken)
    {
        var run = await repository.GetByIdAsync(command.RunId, cancellationToken)
                  ?? throw new AgentRunNotFoundException(command.RunId);
        if (!await access.CanCancelAsync(run))
            throw new AgentRunNotFoundException(command.RunId);

        dbContext.ExpectVersion(run, command.Version);

        for (var attempt = 1; ; attempt++)
        {
            run.RequestCancel(timeProvider.GetUtcNow().UtcDateTime);
            try
            {
                await repository.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException) when (command.Version is null && attempt < Attempts)
            {
                // A running run is saved by its worker at every checkpoint. A caller who named no version wants the
                // run stopped whatever its counters are now: read the row again and ask again.
                run.ClearDomainEvents();
                await dbContext.Entry(run).ReloadAsync(cancellationToken);
            }
        }

        if (run.IsTerminal)
            metrics.RecordFinished(run);
        logger.LogInformation("Agent run {RunId}: cancel requested by {UserId} ({Status})", run.Id, access.UserId, run.Status);
        return AgentRunProjections.Map(run);
    }
}
