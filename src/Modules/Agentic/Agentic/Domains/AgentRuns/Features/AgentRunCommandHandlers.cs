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
/// Starts a run for the current caller. Who may start is decided here (the endpoint already required
/// <c>agentic.runs.start</c>; the agent may need its own permission, and it must allow this kind of caller); what the
/// run is allowed to be is decided by <see cref="AgentRunStarter"/>. Nothing runs inside the request.
/// </summary>
public class StartAgentRunHandler(AgentRegistry registry, AgentAccess access, AgentRunStarter starter)
    : ICommandHandler<StartAgentRunCommand, AgentRunDto>
{
    public async Task<AgentRunDto> Handle(StartAgentRunCommand command, CancellationToken cancellationToken)
    {
        var agent = registry.GetAgent(command.AgentKey);
        var definition = agent.Definition;

        // An agent the caller may not start does not exist for them (no probing of agent keys).
        if (!await access.CanStartAsync(definition))
            throw new AgentNotFoundException(command.AgentKey);

        var trigger = access.IsApiKey ? AgentTriggerKind.Api : AgentTriggerKind.User;
        if (!definition.AllowedTriggers.Contains(trigger))
            throw new AgentAccessDeniedException("trigger_not_allowed", definition.Key, trigger);

        var run = await starter.StartAsync(new AgentRunStart(agent, trigger, access.UserId, command.Prompt,
            command.InputJson, AgenticText.CurrentCulture, access.PermissionCeiling, access.ApiKeyId), cancellationToken);
        return AgentRunProjections.Map(run);
    }
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

        // A run that waits for a person is cancelled at once. Its open approval requests are closed right after the
        // save, by CloseApprovalsOnRunFinishedEventHandler (every way a run can finish goes through that handler).
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
