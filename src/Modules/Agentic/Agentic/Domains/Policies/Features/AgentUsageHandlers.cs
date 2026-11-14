using Agentic.Contracts.Usage;

namespace Agentic.Domains.Policies.Features;

public class GetAgentUsageQueryValidator : AbstractValidator<GetAgentUsageQuery>
{
    public const int MaxDays = 366;

    public GetAgentUsageQueryValidator(IStringLocalizer<AgenticResource> localizer)
    {
        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || (x.From < x.To && (x.To.Value - x.From.Value).TotalDays <= MaxDays))
            .WithName(nameof(GetAgentUsageQuery.From))
            .WithMessage(_ => localizer["Validation.Period.Invalid", MaxDays]);
    }
}

public class GetAgentUsageHandler(AgenticDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetAgentUsageQuery, AgentUsageDto>
{
    public async Task<AgentUsageDto> Handle(GetAgentUsageQuery query, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var from = DateTime.SpecifyKind(query.From ?? new DateTime(now.Year, now.Month, 1), DateTimeKind.Utc);
        var to = DateTime.SpecifyKind(query.To ?? from.AddMonths(1), DateTimeKind.Utc);

        var rows = await dbContext.Runs.AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt < to)
            .GroupBy(r => r.AgentKey)
            .Select(g => new
            {
                AgentKey = g.Key,
                Runs = g.Count(),
                Completed = g.Count(r => r.Status == AgentRunStatus.Completed),
                Failed = g.Count(r => r.Status == AgentRunStatus.Failed),
                Cancelled = g.Count(r => r.Status == AgentRunStatus.Cancelled),
                ModelTurns = g.Sum(r => (long)r.ModelTurns),
                ToolCalls = g.Sum(r => (long)r.ToolCalls),
                InputTokens = g.Sum(r => r.InputTokens),
                OutputTokens = g.Sum(r => r.OutputTokens),
                EstimatedCost = g.Sum(r => r.EstimatedCost)
            })
            .OrderBy(x => x.AgentKey)
            .ToListAsync(cancellationToken);

        var byAgent = rows
            .Select(x => new AgentUsageByAgentDto(x.AgentKey, new AgentUsageTotalsDto(x.Runs, x.Completed, x.Failed,
                x.Cancelled, x.Runs - x.Completed - x.Failed - x.Cancelled, x.ModelTurns, x.ToolCalls, x.InputTokens,
                x.OutputTokens, x.EstimatedCost)))
            .ToList();

        var totals = new AgentUsageTotalsDto(
            byAgent.Sum(a => a.Totals.Runs), byAgent.Sum(a => a.Totals.Completed), byAgent.Sum(a => a.Totals.Failed),
            byAgent.Sum(a => a.Totals.Cancelled), byAgent.Sum(a => a.Totals.Active), byAgent.Sum(a => a.Totals.ModelTurns),
            byAgent.Sum(a => a.Totals.ToolCalls), byAgent.Sum(a => a.Totals.InputTokens),
            byAgent.Sum(a => a.Totals.OutputTokens), byAgent.Sum(a => a.Totals.EstimatedCost));

        return new AgentUsageDto(from, to, totals, byAgent);
    }
}
