using System.Globalization;
using Agentic.Data.Configurations;
using Agentic.Domains.AgentRuns.Services;
using Agentic.Runtime.Security;
using Microsoft.Extensions.Options;
using Shared.Core.MultiTenancy.Authorization;

namespace Agentic.Domains.AgentRuns.Features;

public class RequestAgentRunCommandValidator : AbstractValidator<RequestAgentRunCommand>
{
    public RequestAgentRunCommandValidator(IStringLocalizer<AgenticResource> localizer, IOptions<AgenticOptions> options)
    {
        var limits = options.Value.Limits;

        RuleFor(x => x.AgentKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.AgentKey.Required"])
            .MaximumLength(AgentRun.AgentKeyMaxLength).WithMessage(_ => localizer["Validation.AgentKey.MaxLength"]);
        RuleFor(x => x.Trigger)
            .Must(t => t is AgentTriggerKind.Event or AgentTriggerKind.Schedule)
            .WithMessage(_ => localizer["Validation.Trigger.Automatic"]);
        RuleFor(x => x.TriggerKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.TriggerKey.Required"])
            .MaximumLength(AgentRun.TriggerKeyMaxLength).WithMessage(_ => localizer["Validation.TriggerKey.MaxLength"]);
        RuleFor(x => x.OnBehalfOfUserId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Prompt)
            .MaximumLength(limits.MaxPromptLength).WithMessage(_ => localizer["Validation.Prompt.MaxLength", limits.MaxPromptLength]);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Prompt) || !string.IsNullOrWhiteSpace(x.InputJson))
            .WithName(nameof(RequestAgentRunCommand.Prompt))
            .WithMessage(_ => localizer["Validation.Input.Required"]);
        RuleFor(x => x.InputJson)
            .Must(AgentJson.IsJsonObject).WithMessage(_ => localizer["Validation.Input.NotJsonObject"])
            .When(x => !string.IsNullOrWhiteSpace(x.InputJson));
    }
}

/// <summary>
/// Starts a run for an automatic trigger (an event consumer or a recurring job of the module that owns the agent).
/// There is no request and no caller to authorize, so everything is explicit:
/// <list type="bullet">
/// <item><b>Trigger policy.</b> The agent's definition must list the trigger kind, and the tenant's policy must allow
/// automatic triggers.</item>
/// <item><b>A real user.</b> The run acts for <c>OnBehalfOfUserId</c>, who must be an active member holding
/// <c>agentic.runs.start</c> and the agent's own permission right now. There is no system principal: an automatic run
/// can do nothing its user could not do by hand, and every tool call re-checks them like any other run.</item>
/// <item><b>Once per trigger.</b> <c>TriggerKey</c> is unique per tenant and agent: a redelivered or duplicated
/// trigger returns the run that already exists.</item>
/// <item><b>Bounded fan-out.</b> At most <c>Agentic:Triggers:MaxAutomaticRunsPerHour</c> automatic runs per agent and
/// tenant; beyond that the request is refused (a consumer that rethrows is redelivered later), so an event loop
/// cannot start runs without limit.</item>
/// </list>
/// </summary>
public class RequestAgentRunHandler(
    AgentRegistry registry,
    AgentRunStarter starter,
    AgentPrincipalResolver principals,
    AgenticDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    ILogger<RequestAgentRunHandler> logger) : ICommandHandler<RequestAgentRunCommand, RequestAgentRunResult>
{
    public async Task<RequestAgentRunResult> Handle(RequestAgentRunCommand command, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            throw new AgenticDisabledException();

        var agent = registry.GetAgent(command.AgentKey);
        var definition = agent.Definition;
        if (!definition.AllowedTriggers.Contains(command.Trigger))
            throw new AgentAccessDeniedException("trigger_not_allowed", definition.Key, command.Trigger);

        if (await FindAsync(definition.Key, command.TriggerKey, cancellationToken) is { } existing)
            return new RequestAgentRunResult(existing, Created: false);

        var principal = await principals.ResolveAsync(command.OnBehalfOfUserId, null, cancellationToken);
        if (principal is null ||
            !principal.Permissions.Contains(TenantPermissions.AgentRunsStart) ||
            (definition.RequiredPermission is { } required && !principal.Permissions.Contains(required)))
        {
            throw new AgentAccessDeniedException("on_behalf_of_not_authorized", definition.Key);
        }

        await EnsureRateAsync(definition.Key, cancellationToken);

        // The run's user is the "current user" of what follows (audit columns, the single-tenant plan subscriber).
        using var actingUser = ActingUser.Begin(command.OnBehalfOfUserId);
        try
        {
            var run = await starter.StartAsync(new AgentRunStart(agent, command.Trigger, command.OnBehalfOfUserId,
                command.Prompt, command.InputJson, AgenticText.NormalizeCulture(CultureInfo.CurrentUICulture.Name),
                TriggerKey: command.TriggerKey), cancellationToken);
            return new RequestAgentRunResult(run.Id, Created: true);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(AgentRunConfiguration.TriggerIndexName))
        {
            // The same trigger arrived twice at the same moment: the other delivery created the run.
            var winner = await FindAsync(definition.Key, command.TriggerKey, cancellationToken);
            if (winner is null)
                throw;
            logger.LogInformation("Agent run for trigger {TriggerKey} of {Agent} already exists ({RunId})",
                command.TriggerKey, definition.Key, winner);
            return new RequestAgentRunResult(winner.Value, Created: false);
        }
    }

    private async Task<Guid?> FindAsync(string agentKey, string triggerKey, CancellationToken cancellationToken)
    {
        var id = await dbContext.Runs.AsNoTracking()
            .Where(r => r.AgentKey == agentKey && r.TriggerKey == triggerKey)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id;
    }

    private async Task EnsureRateAsync(string agentKey, CancellationToken cancellationToken)
    {
        var limit = options.Value.Triggers.MaxAutomaticRunsPerHour;
        if (limit <= 0)
            return;

        var since = timeProvider.GetUtcNow().UtcDateTime.AddHours(-1);
        var recent = await dbContext.Runs.CountAsync(r =>
            r.AgentKey == agentKey &&
            (r.TriggerKind == AgentTriggerKind.Event || r.TriggerKind == AgentTriggerKind.Schedule) &&
            r.CreatedAt >= since, cancellationToken);
        if (recent >= limit)
            throw new AgentRunLimitException("automatic_run_limit", limit);
    }
}
