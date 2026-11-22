using Agentic.Domains.AgentRuns.Services;

namespace Agentic.Domains.AgentRuns.Features;

// ----- agents ---------------------------------------------------------------------------------------------------

public class GetAgentsHandler(AgentRegistry registry, AgentAccess access, AgenticDbContext dbContext)
    : IQueryHandler<GetAgentsQuery, IReadOnlyList<AgentDto>>
{
    public async Task<IReadOnlyList<AgentDto>> Handle(GetAgentsQuery query, CancellationToken cancellationToken)
    {
        // Agents the tenant switched off are not offered.
        var disabled = await dbContext.Policies.AsNoTracking()
            .Where(p => !p.Enabled).Select(p => p.AgentKey).ToListAsync(cancellationToken);

        var agents = new List<AgentDto>();
        foreach (var agent in registry.Agents.OrderBy(a => a.Definition.Key, StringComparer.Ordinal))
        {
            if (!disabled.Contains(agent.Definition.Key) && await access.CanStartAsync(agent.Definition))
                agents.Add(AgentRunProjections.Map(agent.Definition));
        }

        return agents;
    }
}

public class GetAgentByKeyQueryValidator : AbstractValidator<GetAgentByKeyQuery>
{
    public GetAgentByKeyQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.Key)
            .NotEmpty().WithMessage(_ => localizer["Validation.AgentKey.Required"])
            .MaximumLength(AgentRun.AgentKeyMaxLength).WithMessage(_ => localizer["Validation.AgentKey.MaxLength"]);
    }
}

public class GetAgentByKeyHandler(AgentRegistry registry, AgentAccess access)
    : IQueryHandler<GetAgentByKeyQuery, AgentDetailDto>
{
    public async Task<AgentDetailDto> Handle(GetAgentByKeyQuery query, CancellationToken cancellationToken)
    {
        var agent = registry.GetAgent(query.Key);
        if (!await access.CanStartAsync(agent.Definition))
            throw new AgentNotFoundException(query.Key);

        var tools = agent.Tools
            .Select(t => new AgentToolDto(t.Name, t.Version, t.Description, t.Risk, t.Permission))
            .ToList();
        return new AgentDetailDto(AgentRunProjections.Map(agent.Definition), tools);
    }
}

// ----- runs -----------------------------------------------------------------------------------------------------

public class GetAgentRunsQueryValidator : AbstractValidator<GetAgentRunsQuery>
{
    public GetAgentRunsQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
        RuleFor(x => x.Status).IsInEnum().WithMessage(_ => localizer["Validation.Status.Invalid"])
            .When(x => x.Status.HasValue);
        RuleFor(x => x.AgentKey).MaximumLength(AgentRun.AgentKeyMaxLength)
            .WithMessage(_ => localizer["Validation.AgentKey.MaxLength"]);
    }
}

public class GetAgentRunsHandler(AgenticDbContext dbContext, AgentAccess access)
    : IQueryHandler<GetAgentRunsQuery, PaginatedResult<AgentRunDto>>
{
    public async Task<PaginatedResult<AgentRunDto>> Handle(GetAgentRunsQuery query, CancellationToken cancellationToken)
    {
        var runs = dbContext.Runs.AsNoTracking().Where(await access.ReadableAsync());
        if (!string.IsNullOrWhiteSpace(query.AgentKey))
            runs = runs.Where(r => r.AgentKey == query.AgentKey);
        if (query.Status is { } status)
            runs = runs.Where(r => r.Status == status);
        if (query.ConversationId is { } conversationId)
            runs = runs.Where(r => r.ConversationId == conversationId);

        return await runs
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(AgentRunProjections.ToDto)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

public class GetAgentRunByIdHandler(AgenticDbContext dbContext, AgentAccess access)
    : IQueryHandler<GetAgentRunByIdQuery, AgentRunDetailDto>
{
    public async Task<AgentRunDetailDto> Handle(GetAgentRunByIdQuery query, CancellationToken cancellationToken)
    {
        var readable = dbContext.Runs.AsNoTracking()
            .Where(await access.ReadableAsync())
            .Where(r => r.Id == query.RunId);

        var run = await readable.Select(AgentRunProjections.ToDto).FirstOrDefaultAsync(cancellationToken)
                  ?? throw new AgentRunNotFoundException(query.RunId);
        var content = await readable
            .Select(r => new { r.Prompt, r.InputJson, r.OutputJson, r.OutputText })
            .FirstAsync(cancellationToken);

        return new AgentRunDetailDto(run, content.Prompt, content.InputJson, content.OutputJson, content.OutputText);
    }
}

public class GetAgentRunStepsQueryValidator : AbstractValidator<GetAgentRunStepsQuery>
{
    public GetAgentRunStepsQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
    }
}

public class GetAgentRunStepsHandler(AgenticDbContext dbContext, AgentAccess access)
    : IQueryHandler<GetAgentRunStepsQuery, PaginatedResult<AgentRunStepDto>>
{
    public async Task<PaginatedResult<AgentRunStepDto>> Handle(GetAgentRunStepsQuery query, CancellationToken cancellationToken)
    {
        // The run decides who may read its transcript: someone else's run is not found, steps included.
        var readable = await dbContext.Runs.AsNoTracking()
            .Where(await access.ReadableAsync())
            .AnyAsync(r => r.Id == query.RunId, cancellationToken);
        if (!readable)
            throw new AgentRunNotFoundException(query.RunId);

        return await dbContext.Steps.AsNoTracking()
            .Where(s => s.RunId == query.RunId)
            .OrderBy(s => s.Sequence)
            .Select(AgentRunProjections.StepToDto)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
