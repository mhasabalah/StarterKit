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

        // Model calls outside a run (conversation summaries), counted for the agent they were made for.
        var calls = await dbContext.ModelUsages.AsNoTracking()
            .Where(u => u.CreatedAt >= from && u.CreatedAt < to)
            .GroupBy(u => u.AgentKey)
            .Select(g => new
            {
                AgentKey = g.Key,
                Summaries = g.Count(u => u.Kind == AgentModelUsageKind.ConversationSummary),
                InputTokens = g.Sum(u => u.InputTokens),
                OutputTokens = g.Sum(u => u.OutputTokens),
                EstimatedCost = g.Sum(u => u.EstimatedCost)
            })
            .ToDictionaryAsync(x => x.AgentKey, cancellationToken);

        var byAgent = rows.Select(x => x.AgentKey).Union(calls.Keys).Order(StringComparer.Ordinal)
            .Select(key =>
            {
                var run = rows.FirstOrDefault(r => r.AgentKey == key);
                calls.TryGetValue(key, out var call);
                var runs = run?.Runs ?? 0;
                var completed = run?.Completed ?? 0;
                var failed = run?.Failed ?? 0;
                var cancelled = run?.Cancelled ?? 0;
                var callInput = call?.InputTokens ?? 0;
                var callOutput = call?.OutputTokens ?? 0;
                var callCost = call?.EstimatedCost ?? 0;
                return new AgentUsageByAgentDto(key, new AgentUsageTotalsDto(runs, completed, failed, cancelled,
                    runs - completed - failed - cancelled, run?.ModelTurns ?? 0, run?.ToolCalls ?? 0,
                    (run?.InputTokens ?? 0) + callInput, (run?.OutputTokens ?? 0) + callOutput,
                    (run?.EstimatedCost ?? 0) + callCost, call?.Summaries ?? 0, callInput + callOutput, callCost));
            })
            .ToList();

        var totals = new AgentUsageTotalsDto(
            byAgent.Sum(a => a.Totals.Runs), byAgent.Sum(a => a.Totals.Completed), byAgent.Sum(a => a.Totals.Failed),
            byAgent.Sum(a => a.Totals.Cancelled), byAgent.Sum(a => a.Totals.Active), byAgent.Sum(a => a.Totals.ModelTurns),
            byAgent.Sum(a => a.Totals.ToolCalls), byAgent.Sum(a => a.Totals.InputTokens),
            byAgent.Sum(a => a.Totals.OutputTokens), byAgent.Sum(a => a.Totals.EstimatedCost),
            byAgent.Sum(a => a.Totals.Summaries), byAgent.Sum(a => a.Totals.SummaryTokens),
            byAgent.Sum(a => a.Totals.SummaryCost));

        return new AgentUsageDto(from, to, totals, byAgent);
    }
}
