using Agentic.Contracts.Policies;

namespace Agentic.Domains.Policies.Features;

public class GetAgentPolicyQueryValidator : AbstractValidator<GetAgentPolicyQuery>
{
    public GetAgentPolicyQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.AgentKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.AgentKey.Required"])
            .MaximumLength(AgentRun.AgentKeyMaxLength).WithMessage(_ => localizer["Validation.AgentKey.MaxLength"]);
    }
}

public class GetAgentPolicyHandler(AgenticDbContext dbContext, AgentRegistry registry)
    : IQueryHandler<GetAgentPolicyQuery, AgentPolicyDto>
{
    public async Task<AgentPolicyDto> Handle(GetAgentPolicyQuery query, CancellationToken cancellationToken)
    {
        var agent = registry.GetAgent(query.AgentKey);
        var policy = await dbContext.Policies.AsNoTracking()
            .FirstOrDefaultAsync(p => p.AgentKey == agent.Definition.Key, cancellationToken);
        return AgentPolicyMapping.Map(agent.Definition.Key, policy);
    }
}

public class UpdateAgentPolicyCommandValidator : AbstractValidator<UpdateAgentPolicyCommand>
{
    public UpdateAgentPolicyCommandValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.AgentKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.AgentKey.Required"])
            .MaximumLength(AgentRun.AgentKeyMaxLength).WithMessage(_ => localizer["Validation.AgentKey.MaxLength"]);
        RuleFor(x => x.AutonomyCeiling).IsInEnum().WithMessage(_ => localizer["Validation.Autonomy.Invalid"])
            .When(x => x.AutonomyCeiling.HasValue);
        RuleFor(x => x.MaxModelTurns).GreaterThan(0).WithMessage(_ => localizer["Validation.Limit.Positive"])
            .When(x => x.MaxModelTurns.HasValue);
        RuleFor(x => x.MaxToolCalls).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Limit.NotNegative"])
            .When(x => x.MaxToolCalls.HasValue);
        RuleFor(x => x.MaxTokens).GreaterThan(0).WithMessage(_ => localizer["Validation.Limit.Positive"])
            .When(x => x.MaxTokens.HasValue);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

/// <summary>Creates or updates the tenant's policy of a registered agent (the endpoint requires <c>agentic.manage</c>).</summary>
public class UpdateAgentPolicyHandler(
    IAgentPolicyRepository repository,
    AgenticDbContext dbContext,
    AgentRegistry registry,
    ILogger<UpdateAgentPolicyHandler> logger) : ICommandHandler<UpdateAgentPolicyCommand, AgentPolicyDto>
{
    public async Task<AgentPolicyDto> Handle(UpdateAgentPolicyCommand command, CancellationToken cancellationToken)
    {
        var agentKey = registry.GetAgent(command.AgentKey).Definition.Key;
        var settings = new AgentPolicySettings(command.Enabled, command.AutonomyCeiling, command.AllowAutomaticTriggers,
            command.RequireApprovalForChanges, command.MaxModelTurns, command.MaxToolCalls, command.MaxTokens);

        var policy = await repository.GetByAgentKeyAsync(agentKey, cancellationToken);
        if (policy is null)
        {
            policy = AgentPolicy.Create(agentKey, settings);
            repository.Add(policy);
        }
        else
        {
            dbContext.ExpectVersion(policy, command.Version);
            policy.Update(settings);
        }

        await repository.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Agent policy of {Agent} set: enabled {Enabled}, autonomy ceiling {Ceiling}", agentKey,
            policy.Enabled, policy.AutonomyCeiling);
        return AgentPolicyMapping.Map(agentKey, policy);
    }
}

public static class AgentPolicyMapping
{
    public static AgentPolicyDto Map(string agentKey, AgentPolicy? policy)
    {
        var settings = policy?.Settings ?? AgentPolicySettings.Default;
        return new AgentPolicyDto(agentKey, settings.Enabled, settings.AutonomyCeiling, settings.AllowAutomaticTriggers,
            settings.RequireApprovalForChanges, settings.MaxModelTurns, settings.MaxToolCalls, settings.MaxTokens,
            IsDefault: policy is null, policy?.Version ?? 0);
    }
}
